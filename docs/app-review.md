q# App Review Guide

A walkthrough of the Svalboard Layer Viz app for review purposes. Assumes no protocol knowledge. Detailed on UI + service split and reasoning; broad strokes on USB/Vial internals.

If you spot something in here that doesn't match the code, the code is the truth — this doc rots faster than the implementation.

## Table of contents

1. [What this app does](#1-what-this-app-does)
2. [Project layout](#2-project-layout)
3. [Protocol layer — broad strokes](#3-protocol-layer--broad-strokes)
4. [Core services — detailed](#4-core-services--detailed)
5. [App composition root](#5-app-composition-root)
6. [ViewModel hierarchy](#6-viewmodel-hierarchy)
7. [View structure](#7-view-structure)
8. [Data flow: device → screen](#8-data-flow-device--screen)
9. [Device lifecycle](#9-device-lifecycle)
10. [Styling and colors](#10-styling-and-colors)
11. [Key files reference](#11-key-files-reference)
12. [Testing](#12-testing)
13. [Rethink candidates](#13-rethink-candidates)

---

## 1. What this app does

Reads the keymap from a Svalboard (a 60-key ergonomic keyboard running QMK firmware) over USB, displays all layers visually, highlights live key presses in real time, lets the user edit the keymap (keys, macros, combos, tap-dances, QMK settings), and exports layouts to PNG/PDF/SVG. Built on .NET 10 with Avalonia UI (cross-platform XAML), MVVM architecture, CommunityToolkit.Mvvm for source-generated observable properties.

Single window, single device, desktop app. Windows, macOS, Linux.

---

## 2. Project layout

Three projects in the solution:

- [src/SvalboardLayerViz.Core/](src/SvalboardLayerViz.Core/) — zero UI dependencies. All protocol, keymap, layout, export, settings, and history logic lives here.
- [src/SvalboardLayerViz.App/](src/SvalboardLayerViz.App/) — Avalonia views (AXAML) + ViewModels. Composition root. Dialog factories. Global hotkey and platform-specific concerns.
- [src/SvalboardLayerViz.Tests/](src/SvalboardLayerViz.Tests/) — xUnit. ~1188 tests across Core services and App ViewModels.

### Why split this way

Core is unit-testable headless — the test suite instantiates `VialProtocolService`, `KeymapLoader`, `BoardLayoutComputer` etc. without bringing up Avalonia. No UI reference means no threading-model assumptions leak into the model layer. If someone later wants a CLI keymap inspector or a web-based visualizer, Core drops in without modification.

App is the only project that references Avalonia, HidSharp (USB HID), SkiaSharp (export rendering), and SharpHook (global hotkeys on Windows/macOS). Keeping those platform concerns out of Core means tests don't drag in GUI toolkits and the Core assembly stays portable.

The split also enforces discipline: a feature that slips UI concerns into Core (e.g., an `ObservableCollection` in a service) breaks the build. That's a useful fence.

---

## 3. Protocol layer — broad strokes

You don't need to know any of this to review the UI code. Skim this section, trust the tests, move on.

**The wire.** The Svalboard shows up as a USB HID device with a Vial-specific auxiliary interface (usage page `0xFF60`). All communication uses 32-byte fixed-size "envelopes" — request and response both fit in 32 bytes. This is a USB HID constraint: keyboards need low-latency fixed-size packets.

**The protocol.** Vial is a superset of VIA, which is itself a common QMK protocol for userspace keyboard configuration. Commands are a single byte (e.g., `0x08` = lighting, `0x09` = custom values) followed by subcommand and payload. The firmware responds in the same envelope shape. Every send-receive pair is atomic: [VialProtocolService](src/SvalboardLayerViz.Core/Protocol/VialProtocolService.cs) wraps a lock around write-then-read so two background polling threads can't interleave reports.

**What gets loaded at connect time.** [KeymapLoader](src/SvalboardLayerViz.Core/Keymap/KeymapLoader.cs) orchestrates it:

1. Device ID + firmware version + Vial protocol version handshake.
2. Layer count (how many layers does this firmware build support).
3. Layout definition — an XZ-compressed JSON describing the matrix dimensions (10×6), physical key positions, and custom keycode declarations. [XzDecompressor](src/SvalboardLayerViz.Core/Protocol/VialProtocolService.cs) inflates it in-memory.
4. Keymap buffer — a 3-D array indexed `[layer][row][col]` → 16-bit keycode. Fetched in chunks of 28 bytes per envelope (since some header metadata lives in the envelope too).
5. Macros, combos, tap-dances, QMK settings — each fetched with its own command.

**The 16-bit keycodes.** Each cell is a `ushort`. Low values (`0x0004` = A, `0xE0` = Ctrl) are basic keys. Higher ranges encode composite meanings: `0x2000–0x3FFF` is modifier+key (shift-A, ctrl-Z), `0x4000–0x4FFF` is layer-tap (hold for layer, tap for key), `0x5000–0x5FFF` is mod-tap, `0x5200–0x52FF` is layer-mod, `0x7E00–0x7FFF` is user-defined custom keycodes. [KeycodeService](src/SvalboardLayerViz.Core/Keymap/KeycodeService.cs) and [KeycodeCodec](src/SvalboardLayerViz.Core/Keymap/KeycodeCodec.cs) resolve these numbers to display labels.

**Real-time polling (optional).** When the user enables live highlighting, [MatrixPollingService](src/SvalboardLayerViz.Core/Protocol/MatrixPollingService.cs) asks the firmware "what's the current switch matrix state?" about 10× per second. The response is a bitmap of which keys are pressed. When the firmware supports it, [LedPollingService](src/SvalboardLayerViz.Core/Protocol/LedPollingService.cs) also polls the rgblight color — this is how the app knows which layer is active even when layer switching happens from firmware-internal logic (combos, mouse layer) that the matrix heuristic can't see.

**Editing.** When the user edits a key, the app sends one "set keycode" command per change, per cell. Saves are batched via [SaveFlow](src/SvalboardLayerViz.Core/Keymap/SaveFlow.cs) which handles validation, partial-failure recovery, and post-save reload.

**Why it's tricky.** The 32-byte envelope forces chunking everywhere. Byte order is inconsistent (big-endian in some places, little-endian in others) because QMK has a long history targeting different hardware. XZ compression exists to save flash space on the microcontroller. Coverage of these parts lives in [src/SvalboardLayerViz.Tests/Protocol/](src/SvalboardLayerViz.Tests/Protocol/) — 116+ tests around command encoding/decoding, buffer size caps, and atomicity guarantees. Trust those; they were rewritten several times during hostile review.

---

## 4. Core services — detailed

Core is organized by concern, one folder per area:

```
Core/
├── Protocol/       — USB HID transport + Vial protocol
├── Device/         — HID enumeration + connect orchestration
├── Keymap/         — keymap model, keycode resolution, edit/save, colors
├── Layout/         — physical key positions, board layout computation
├── Macros/         — macro codec
├── Dynamic/        — combos and tap-dances codecs
├── Export/         — PNG/PDF/SVG rendering
├── History/        — snapshots and diffs
├── Settings/       — user preferences persistence
├── Persistence/    — atomic file I/O primitives
├── Diagnostics/    — in-app log for device events
├── QmkSettings/    — QMK settings catalog + save ops
└── Models/         — plain data records (Key, Layer, KeyboardConfig)
```

### Protocol + device

[VialProtocolService](src/SvalboardLayerViz.Core/Protocol/VialProtocolService.cs) (764 lines)

Owns the USB HID transport and Vial command encoding. The public surface is an `IVialProtocolService` interface so tests can fake the whole thing. Every public method is a one-shot send-receive: `GetDeviceId()`, `GetLayerCount()`, `GetKeymapBuffer(layers, rows, cols)`, `GetSwitchMatrixState(rows, cols)`, `SetKeycode(...)`, etc.

Internally, `SendCommand(byte[])` is wrapped in a `lock (_sendLock)` block. Without this, a matrix poll happening in parallel with a save would corrupt both — the firmware can't tell whose 32-byte response is whose if two requests overlap. The lock is the single choke point for all HID I/O. Session 18-04-26-d also added OOM caps (`MaxDefinitionSize`, `MaxKeymapSize`, `MaxMacroSize` at 64 KB each) so a malicious or corrupt firmware response can't allocate gigabytes.

Why the single class: transport and protocol encoding are tightly coupled in practice. Every Vial command has its own response format; separating them would require a generic "send bytes, get bytes" interface with payload parsing scattered across 40+ wrapper methods. It's on the rethink list (§13) if a second backend ever materializes.

[MatrixPollingService](src/SvalboardLayerViz.Core/Protocol/MatrixPollingService.cs) and [LedPollingService](src/SvalboardLayerViz.Core/Protocol/LedPollingService.cs)

Two event-driven pollers, each with its own background thread. Each one:

1. Holds a reference to `IVialProtocolService` (so they share the send-lock).
2. Runs a `PollLoop()` at ~10 Hz (`Thread.Sleep(100)`).
3. Compares the new reading against the previous one; if different, fires an event (`MatrixStateChanged`, `LedColorChanged`) on a UI dispatcher.
4. Exits when `_disposed` flips. Session 18-04-26-e marked `_disposed` as `volatile` — without that, the JIT can cache the field in a register and the thread never sees the exit signal.

Why separate services: matrix polling and LED polling have different lifetimes and different capabilities (not all firmware supports the LED poll). They also fire different events that route to different VM handlers. Keeping them parallel classes makes the polling contract symmetric and the VM subscription code uniform.

[DeviceConnectionService](src/SvalboardLayerViz.Core/Device/DeviceConnectionService.cs)

Wraps HidSharp's global `DeviceList.Local`. Exposes `FindVialDevices()` (filters to usage page `0xFF60`) and `OnDeviceListChanged(Action)` (subscribes to USB hotplug events). Separated from `VialProtocolService` because device *discovery* and device *conversation* are different concerns — discovery happens once, fails fast if nothing's plugged in; conversation happens continuously after a device is picked.

[DeviceSession](src/SvalboardLayerViz.Core/Device/DeviceSession.cs)

Pure orchestration: given an `IVialProtocolService` and `DeviceConnectionService`, connect to the preferred device and return a fully-loaded `KeyboardConfig`. Pulled out of `MainWindowViewModel` during session 17-04-26 so the connect sequence is testable without the VM.

### Keymap and model

[KeymapLoader](src/SvalboardLayerViz.Core/Keymap/KeymapLoader.cs)

One-shot "load everything." Given a connected `IVialProtocolService`, returns a `KeyboardConfig` with all layers, macros, combos, tap-dances, QMK settings, custom keycodes, and layer colors. Wraps the 6-step download sequence. The VM doesn't need to know about chunking or decompression — it gets a populated model object.

[KeycodeService](src/SvalboardLayerViz.Core/Keymap/KeycodeService.cs) + [KeycodeCodec](src/SvalboardLayerViz.Core/Keymap/KeycodeCodec.cs) + [KeycodeDecoder](src/SvalboardLayerViz.Core/Keymap/KeycodeDecoder.cs)

Three layers:

1. `KeycodeCodec` — unified encode/decode of 16-bit keycodes (consolidated in session 17-04-26; was previously two separate encoder/decoder files). Pure function of keycode bytes.
2. `KeycodeDecoder` — breaks a keycode into a structured `KeycodeDescriptor` (range, modifier bits, layer index, tap code).
3. `KeycodeService` — combines the decoder with user-facing data (custom keycode labels, macro previews, user-defined per-key labels) to produce display strings.

`KeycodeService` is stateful. `SetCustomKeycodes()`, `SetMacroPreviews()`, `SetCustomKeyLabels()` must be called before `Resolve()` or custom keycodes fall back to their hex representation. This is a minor smell — see §13.

[LayerSwitchService](src/SvalboardLayerViz.Core/Keymap/LayerSwitchService.cs)

Given the current switch matrix state + the keymap, compute which layer is active. Tracks momentary (MO/LT) and toggle (TG) keys, applies a configurable hold threshold to suppress noise, and resolves the highest active layer. Stateless from the VM's perspective (the VM calls `Update(matrix)` each poll tick) — state lives inside the service. Pulled out of the VM in session 17-04-26 so the layer-resolution logic can be unit-tested without any UI.

[TransparentKeyResolver](src/SvalboardLayerViz.Core/Keymap/TransparentKeyResolver.cs)

Walks the layer stack downward to resolve `KC_TRNS` keys. If layer 3 has a transparent key at (row 0, col 0), this service walks layer 2, 1, 0 looking for the first non-transparent value. Returns that as the "effective" key. Optimized from O(keys²) to O(keys·layers) in session 16-04-26.

[LayerColorService](src/SvalboardLayerViz.Core/Keymap/LayerColorService.cs) + [LayerColorPalette](src/SvalboardLayerViz.App/ViewModels/LayerColorPalette.cs)

Color generation lives in Core (`LayerColorService`) and is cached in App (`LayerColorPalette`). The service has three modes:

- **Algorithmic** — evenly-spaced hues starting at 210° (blue), saturation 0.55, lightness 0.50. Layer 0 gets blue, layer 1 gets the next step, etc.
- **Device-supplied** — reads HSV from firmware (set via rgblight) and converts to our hex palette.
- **User-override** — per-layer color from settings. Takes precedence over the other two.

Output is a `LayerColors` record with six hex strings (Bg, Border, Accent, TransBg, TransBorder, TextColor). The palette precomputes all N layers once per keymap load so `KeyViewModel` has O(1) access during binding.

### Editing and saving

[KeymapEditSession](src/SvalboardLayerViz.Core/Keymap/KeymapEditSession.cs) (415 lines)

In-memory buffer of pending edits. Tracks a stack of `EditOp` records (SetKeycodeOp, SetMacroOp, SetComboOp, SetTapDanceOp, SetQmkSettingOp). Supports undo via a `List<EditOp>` history with `_undoIndex`. `Dirty` exposes a count of pending ops.

The session doesn't touch the device directly. It just accumulates changes. When the user hits "save," `SaveFlow` reads the op list and dispatches to the protocol service.

Known structural smell: `ExecuteForward` and `ExecuteReverse` are mirror switch statements over `EditOp` subtypes. See §13.

[SaveFlow](src/SvalboardLayerViz.Core/Keymap/SaveFlow.cs) + [SaveFlowExecutor](src/SvalboardLayerViz.Core/Keymap/SaveFlowExecutor.cs) + [SaveReconciliation](src/SvalboardLayerViz.Core/Keymap/SaveReconciliation.cs) + [PreSaveSafetyCheck](src/SvalboardLayerViz.Core/Keymap/PreSaveSafetyCheck.cs)

Staged save pipeline:

1. `PreSaveSafetyCheck` — validates that the pending edit set is firmware-safe (no isolation issues, no macro buffer overflow, no invalid keycodes). Fails loud before anything hits the wire.
2. `SaveFlow` — high-level coordination: "save edits, then reload from device to verify."
3. `SaveFlowExecutor` — iterates ops, sends each to the device, handles per-op failures.
4. `SaveReconciliation` — after the write, compares what the device reports back against what we tried to write. Used for partial-failure recovery.

Pulled into Core in session 17-04-26 so the whole save lifecycle has test coverage without needing the VM.

[SnapshotService](src/SvalboardLayerViz.Core/History/SnapshotService.cs)

Captures the current `KeyboardConfig` as a JSON file on disk. Categorized by reason: manual, pre-save, post-save, first-connect. Supports listing, loading, pruning old snapshots. Enables the "History" dialog which shows a diff of any past snapshot against the current state.

[SettingsService](src/SvalboardLayerViz.Core/Settings/SettingsService.cs) + [AtomicFile](src/SvalboardLayerViz.Core/Persistence/AtomicFile.cs)

`SettingsService` loads and saves user preferences (window geometry, language, hotkey, layer colors, live-highlight toggle) as a single JSON file. `AtomicFile` provides write-then-rename with an explicit fsync before rename — added in session 18-04-26-d to close the power-loss window where the file rename succeeds but the data hasn't hit disk.

### Dynamic entries (macros, combos, tap-dances)

[MacroCodec](src/SvalboardLayerViz.Core/Macros/MacroCodec.cs), [ComboCodec](src/SvalboardLayerViz.Core/Dynamic/ComboCodec.cs), [TapDanceCodec](src/SvalboardLayerViz.Core/Dynamic/TapDanceCodec.cs)

Each codec decodes the device's byte buffer into a list of structured actions (tap X, hold Y, delay 500ms, send string "hello") and re-encodes a user-edited list back into bytes. Complex because macros mix action bytes and literal text bytes in a single stream. There's one known edge case around byte value `0x01` — see §13.

### Export

[ExportService](src/SvalboardLayerViz.Core/Export/ExportService.cs) + [BoardRenderer](src/SvalboardLayerViz.Core/Export/BoardRenderer.cs) + [KeyStyleResolver](src/SvalboardLayerViz.Core/Export/KeyStyleResolver.cs)

`BoardRenderer` draws a keyboard layout using SkiaSharp primitives (path, fill, stroke, text). It iterates hands → clusters → keys using the same `BoardLayoutComputer` output as the UI.

`KeyStyleResolver` is shared between the UI (`KeyViewModel`) and the export (`BoardRenderer`). Given a Key, a Layer, and a LayerColorPalette, it returns the color set and opacity that should be rendered. Shared so print matches screen. There's a "print-friendly" variant that returns lighter pastels — chosen via a flag in `ExportOptions`.

---

## 5. App composition root

The App project's job is to wire Core services to views. No IoC container — manual constructor injection with defaults.

### Entry point

[App.axaml.cs](src/SvalboardLayerViz.App/App.axaml.cs) (813 lines) overrides `OnFrameworkInitializationCompleted`. Simplified flow:

```
1. Install exception handlers (AppDomain, TaskScheduler, UIThread)
2. Create SettingsService, load settings, configure culture
3. Create MainWindowViewModel(settingsService)
4. Create MainWindow, set DataContext, restore window geometry
5. Wire ~20 Action callbacks from VM to dialog factories
6. Create GlobalHotkeyService if not Linux; wire F12 → toggle window
7. Show MainWindow
8. Schedule InitializeDeviceConnection on background thread (deferred)
```

The deferred device connect is important. HID enumeration on macOS can take ~500ms on the first call; blocking the UI thread during window show is visibly laggy. Scheduling it after the window is painted keeps startup snappy.

### How ViewModels get their services

`MainWindowViewModel`'s constructor at [MainWindowViewModel.cs:296](src/SvalboardLayerViz.App/ViewModels/MainWindowViewModel.cs#L296) accepts three optional parameters:

```csharp
public MainWindowViewModel(
    ISettingsService? settingsService = null,
    IVialProtocolService? protocolService = null,
    ISnapshotService? snapshotService = null)
```

If any parameter is null, the VM creates a concrete instance. The App layer passes in only `settingsService` (so tests can swap to fake settings); production uses the real `VialProtocolService` and `SnapshotService` via the default path.

Two services are hardcoded as inline fields (not injectable):

```csharp
private readonly DeviceConnectionService _deviceService = new();
private readonly KeycodeService _keycodeService = new();
```

This is inconsistent — see §13 rethink item 3.

### Dialog callbacks: the App/VM seam

The VM doesn't know about windows or dialogs. It exposes `Action` callbacks like `OpenMacrosRequested`, `OpenSettingsRequested`, `ShowKeyPickerRequested`, `QuitRequested`. The App wires each callback to a dialog factory:

```csharp
viewModel.OpenMacrosRequested = () => ShowMacroEditorAsync(viewModel);
viewModel.OpenSettingsRequested = () => ShowSettingsAsync(viewModel);
// ... ~18 more
```

This keeps the VM free of window lifecycle logic (owner, modality, result handling) and keeps the App free of keymap state logic. Dialog VMs are created on demand inside each factory method, populated with data from the main VM, shown as `Dialog.ShowDialog(window)`, and their results are applied back to the main VM on the UI thread. Factory methods also observe faults via `ContinueWith(OnlyOnFaulted)` to log exceptions into the diagnostic log — added in session 18-04-26-d after realizing unobserved dialog-task exceptions were being swallowed by the GC.

### Why no DI container

For a desktop app with one window, one device, and ~8 services, an IoC container adds registration boilerplate and runtime reflection overhead without giving much back. The dependency graph fits on one screen and is explicit. Every service construction is grep-able.

Trade-off accepted: harder to add a second keyboard driver or swap services at runtime (e.g., a "demo mode" without a real device). The alternative — lifting everything to interfaces and registering with Autofac — would be 200 lines of ceremony for a single-window app. Revisit if scope grows.

---

## 6. ViewModel hierarchy

Tree of owned objects:

```
MainWindowViewModel
├── Services (owned)
│   ├── DeviceConnectionService      (inline)
│   ├── IVialProtocolService         (injected or default)
│   ├── KeycodeService               (inline)
│   ├── ISettingsService             (injected)
│   ├── ISnapshotService             (injected)
│   ├── LayerSwitchService           (inline)
│   ├── MatrixPollingService?        (created on connect)
│   └── LedPollingService?           (created on connect if supported)
│
├── Layers: ObservableCollection<LayerViewModel>
│   └── LayerViewModel
│       ├── Layer (model)
│       ├── TabColor (from LayerColorPalette)
│       ├── LeftHand: HandViewModel
│       │   ├── ThumbCluster: KeyClusterViewModel
│       │   │   └── Keys: List<KeyViewModel>    (2–6 thumb keys)
│       │   └── FingerClusters: List<KeyClusterViewModel>
│       │       └── Keys: List<KeyViewModel>    (5 keys per finger cluster)
│       ├── RightHand: HandViewModel  (same structure)
│       └── AllKeys: flat list of KeyViewModel  (for polling fast-path)
│
├── DiagnosticsViewModel
└── Dialog VMs (created on demand by App callbacks, not owned here)
    ├── SettingsViewModel
    ├── HistoryWindowViewModel
    ├── MacroEditorViewModel
    ├── ComboEditorViewModel
    ├── TapDanceEditorViewModel
    ├── PickerSessionViewModel
    ├── UnlockDialogViewModel
    └── ExportDialogViewModel
```

### Key instance stability

`KeyViewModel` instances are stable across saves. When the user saves a keymap edit and the app reloads from the device, the VM tree doesn't rebuild — instead, each `KeyViewModel.UpdateBaseline(newKey)` swaps the underlying model while keeping the VM instance. This matters because:

1. Compiled AXAML bindings hold references to specific VM instances. Rebuilding would force Avalonia to re-resolve bindings, which is slow and can flicker.
2. Creative thumb layouts bind to named slots (`Thumb0` through `Thumb5`) on the cluster VM. Swapping instances would break those bindings.

`UpdateBaseline` also re-runs the `KeyStyleResolver` so colors/opacity reflect the new baseline — added in session 16-04-26 after discovering that colors went stale after save.

### MVVM framework

[CommunityToolkit.Mvvm 8.3.2](https://github.com/CommunityToolkit/dotnet). Source-generated attributes:

- `[ObservableObject]` on a partial class generates the INotifyPropertyChanged boilerplate.
- `[ObservableProperty]` on a private field generates a public property, backing field, and change-notification call.
- `[NotifyPropertyChangedFor(nameof(OtherProp))]` makes changes to this property also raise `PropertyChanged` for `OtherProp` — used for computed properties.
- `[RelayCommand]` on a method generates an `ICommand` property and a backing async/sync implementation.

Compiled Avalonia bindings (`x:DataType` on root elements). No runtime reflection.

---

## 7. View structure

All views in [src/SvalboardLayerViz.App/Views/](src/SvalboardLayerViz.App/Views/). Core visualization hierarchy:

```
MainWindow            (chromeless, layer tabs, status bar)
  └── BoardView       (Canvas + Viewbox centering the two hands)
        ├── HandView (Left)
        │     ├── ThumbClusterView
        │     └── FingerClusterView × 4  (index, middle, ring, pinky)
        │           └── KeyView × 5       (compass layout: N/S/E/W/Down)
        └── HandView (Right)  [same]
```

### [MainWindow.axaml](src/SvalboardLayerViz.App/Views/MainWindow.axaml)

Chromeless window (no native title bar). Grid with:

- A "BarsPanel" at top/bottom — houses the status bar and layer tabs. Auto-flipped based on `IsVerticalLayout` so the status bar reads right-side-up in either orientation.
- A central `DockPanel` hosting `BoardView`.
- Transparency hints and background opacity bound to a slider.

Context menu and keyboard shortcuts wired here.

### [BoardView.axaml](src/SvalboardLayerViz.App/Views/BoardView.axaml)

Centered `Viewbox` containing a `Canvas`. The canvas has two `HandView` instances, positioned by `LeftHandX/LeftHandY/RightHandX/RightHandY` properties on the main VM. Those coordinates are computed based on layout mode (horizontal vs vertical) and hand dimensions. Also includes a "Connect your Svalboard" placeholder visible when `IsConnected` is false.

### [HandView.axaml](src/SvalboardLayerViz.App/Views/HandView.axaml)

Canvas bound to a `HandViewModel`. Renders one `ThumbClusterView` and an `ItemsControl` of `FingerClusterView`s, each positioned via `Canvas.Left/Top` from its cluster VM.

### [FingerClusterView.axaml](src/SvalboardLayerViz.App/Views/FingerClusterView.axaml)

Straightforward. `ItemsControl` with a `Canvas` panel, iterating the cluster's `Keys` list, rendering a `KeyView` for each at the key's local offset.

### [ThumbClusterView.axaml](src/SvalboardLayerViz.App/Views/ThumbClusterView.axaml)

Dual rendering path, controlled by the `ThumbRenderMode` enum:

- **Standard mode** — same as `FingerClusterView`, renders all thumb keys in a generic grid.
- **Creative mode** — hardcoded Viewbox+Border tree for the ergonomic trapezoid layout. Left and right hands each have their own version (mirror images). Each of the six thumb positions (`Thumb0`..`Thumb5`) is a named `Border` with a `<Border.Clip><PathGeometry>` that clips the border to a trapezoid or rounded shape. This is the only place the app uses custom path geometry.

The dual-path design exists because the Svalboard's thumb cluster has overlapping/irregular key shapes that don't fit a simple grid. The "creative" layout is the one most users see. The "standard" layout is a fallback for users who prefer clean grids or for export formats that don't render well with clipping.

### [KeyView.axaml](src/SvalboardLayerViz.App/Views/KeyView.axaml) and [ThumbKeyView.axaml](src/SvalboardLayerViz.App/Views/ThumbKeyView.axaml)

A single key. `Border` with rounded corners, bound to `BackgroundColor`, `BorderColor`, `BorderThickness`, `KeyOpacity`. Inside:

- Primary `DisplayLabel` centered, bold, with a `DropShadowEffect` for contrast.
- Secondary label top-center, dimmed (for mod-tap, layer-tap, etc.).
- Shifted label top-right, subscript (for keys with shift-variant like `!` on `1`).

Context menu wired to `ClickCommand` (pick new keycode) and `SetLabelCommand` (custom per-key label override).

`ThumbKeyView` is the same structure with larger typography (36pt primary, 26pt secondary) and heavier drop shadows — extracted in session 16-04-26 after discovering that creative-layout label refresh was broken because the thumb keys were using a slightly different rendering path than finger keys.

### Converters

Two tiny ones in [src/SvalboardLayerViz.App/Converters/](src/SvalboardLayerViz.App/Converters/):

- `HexColorToBrushConverter` — hex string to `SolidColorBrush` with a safe fallback to dark gray.
- `EqualityToBoolConverter` — parameter-based equality check, used for layer-tab "is selected" state.

### Dialog windows

Eight dialog `.axaml` files for editors and viewers:

- `KeyPickerDialog` — pick a new keycode when editing a key.
- `SettingsWindow` — global preferences.
- `MacroEditorDialog` — edit macro steps.
- `ComboEditorDialog` — edit key combos.
- `TapDanceEditorDialog` — edit tap-dance configurations.
- `HistoryWindow` — list snapshots, open diff dialog.
- `SnapshotDiffDialog` — visual diff of two snapshots.
- `ExportDialog` — export to PNG/PDF/SVG.
- `UnlockDialog` — Vial unlock sequence for secure devices.
- `DiagnosticsWindow` — live matrix state + event log.
- `SafetyConfirmDialog` — confirms a risky save.
- `HelpWindow` — keybindings + attribution.

All dialog VMs are plain `ObservableObject` instances created ad-hoc by App callbacks. Results flow back via callback or `TaskCompletionSource`.

---

## 8. Data flow: device → screen

### On initial load

```
User launches app
   ↓
App.OnFrameworkInitializationCompleted
   ↓
MainWindowViewModel created
   ↓
Window shown
   ↓
InitializeDeviceConnection (deferred, background thread)
   ↓
DeviceConnectionService.FindVialDevices()   ← HidSharp enumeration
   ↓
DeviceSession.Connect()                      ← pick device + open HID stream
   ↓
KeymapLoader.Load()                          ← full 6-step download
   ↓
TransparentKeyResolver.Resolve() per cell    ← walks layer stack
   ↓
LayerColorService.GetLayerColors()           ← generates palette
   ↓
Back on UI thread:
   KeyboardConfig = config
   BuildLayerViewModels() creates tree
   Each LayerViewModel calls BoardLayoutComputer.Compute(layer)
   KeyViewModels get their colors from LayerColorPalette
   ↓
Avalonia bindings resolve
   ↓
Keys appear on screen
```

### On every frame (when live highlighting is on)

```
MatrixPollingService.PollLoop  [background thread, 10 Hz]
   ↓
IVialProtocolService.GetSwitchMatrixState()  ← atomic send/receive with lock
   ↓
bool[rows, cols] state change detected
   ↓
MatrixStateChanged event fires
   ↓
MainWindowViewModel subscriber runs on UI dispatcher
   ↓
KeyViewModel.IsPressed updated for affected keys
   ↓
If auto-layer-switch is on:
   LayerSwitchService.Update(matrix) → new active layer
   SelectedLayerIndex property change
   ↓
Avalonia bindings refresh
   ↓
Keys re-render with highlight / opacity change
```

### On an edit + save

```
User right-clicks a key → KeyView ContextMenu → SetLabelCommand
   ↓
MainWindowViewModel opens KeyPickerDialog (via PickerSessionViewModel)
   ↓
User picks new keycode, dialog closes with result
   ↓
MainWindowViewModel calls KeymapEditSession.Set(row, col, newKeycode)
   ↓
KeyViewModel.PendingKeycode = newKeycode, IsPending = true
   ↓
Dirty count updates, save button enables
   ↓
User clicks Save → RelayCommand
   ↓
PreSaveSafetyCheck.Validate()
   ↓
SaveFlow.Execute()
   ↓
SaveFlowExecutor iterates pending ops
   ↓
For each: IVialProtocolService.SetKeycode(...)   ← writes to device
   ↓
SaveReconciliation reads back, compares
   ↓
SnapshotService captures post-save snapshot
   ↓
KeymapLoader reloads full config from device
   ↓
Each KeyViewModel.UpdateBaseline(newKey)   ← preserves VM identity
   ↓
Screen reflects new state
```

---

## 9. Device lifecycle

### Connect

[MainWindowViewModel.InitializeDeviceConnection](src/SvalboardLayerViz.App/ViewModels/MainWindowViewModel.cs#L315) registers a callback for `DeviceList.Local.Changed` and triggers an initial `TryConnectAsync()`.

`TryConnectAsync` at [MainWindowViewModel.cs:327](src/SvalboardLayerViz.App/ViewModels/MainWindowViewModel.cs#L327):

1. Guard against concurrent attempts — HidSharp fires bursts of device-changed events on hotplug and concurrent HID opens corrupt each other.
2. Dispose-and-replace a CancellationTokenSource (30s timeout per attempt). Assign the new CTS before disposing the old so parallel observers never see null mid-swap.
3. Stop any existing polling services.
4. Run `DeviceSession.Connect` on a background thread.
5. On success: set `KeyboardConfig`, build layer VMs, probe LED poll support, start polling services.
6. On failure: `StatusMessage = "No device found"`, stay idle.

### Disconnect / reconnect

USB unplug raises `DeviceList.Local.Changed`. That triggers `TryConnectAsync` again. Re-enumeration finds nothing, status flips to "No device found," polling services stop. Plugging back in triggers the event again, re-enumeration finds the device, full connect sequence runs.

### Shutdown

Session 18-04-26-e added a robust shutdown path:

- `volatile bool _disposed` in pollers, so the JIT doesn't register-cache the exit check.
- `Interlocked.Exchange` atomic gate on `Shutdown` so `Window.Closing` and `QuitRequested` racing each other can't double-dispose.
- Async `ShutdownAsync()` in App.axaml.cs:
   1. Cancel polling services and wait for their threads to exit (via `ManualResetEventSlim`).
   2. Dispose `VialProtocolService` (closes HID stream).
   3. Save window geometry via `SettingsService`.
   4. Call `Environment.Exit(0)`.

Window's `Closing` event handler is async — cancels the close, runs `ShutdownAsync`, then closes programmatically. This ensures cleanup runs even if the user slams ⌘Q or clicks the red X mid-save.

---

## 10. Styling and colors

Dark theme baseline (Catppuccin-ish):

- Background: `#1E1E2E` / `#181825`
- Text: `#CDD6F4`
- Accents driven by `LayerColorPalette` (per-layer).

Colors are computed in C# code, not in AXAML `StaticResource` lookups. The `MainWindowViewModel` exposes hex-string properties like `BoardBackground` that encode the current opacity slider value and are bound directly to `Background="{Binding BoardBackground}"`. This makes opacity changes instantaneous without re-parsing AXAML.

`LayerColorPalette` is precomputed once per keymap load, storing all N layer color sets in a dictionary. During binding, `KeyViewModel` reads its assigned layer's colors in O(1). If a key is a layer-switch key (MO/TG/LT), it also reads the *target* layer's color for the border, so the user can see at a glance which layer a key jumps to.

For export, the same `KeyStyleResolver` is used with a print-friendly variant flag. That variant returns lighter pastel versions of the palette so exported PDFs don't burn through printer ink on dark fills.

---

## 11. Key files reference

Fast-navigation table. For each concern, the single most important file.

| Concern | File |
|---|---|
| Vial protocol + HID transport | [VialProtocolService.cs](src/SvalboardLayerViz.Core/Protocol/VialProtocolService.cs) |
| Matrix polling | [MatrixPollingService.cs](src/SvalboardLayerViz.Core/Protocol/MatrixPollingService.cs) |
| LED polling | [LedPollingService.cs](src/SvalboardLayerViz.Core/Protocol/LedPollingService.cs) |
| Device enumeration | [DeviceConnectionService.cs](src/SvalboardLayerViz.Core/Device/DeviceConnectionService.cs) |
| Connect orchestration | [DeviceSession.cs](src/SvalboardLayerViz.Core/Device/DeviceSession.cs) |
| Full config load | [KeymapLoader.cs](src/SvalboardLayerViz.Core/Keymap/KeymapLoader.cs) |
| Keycode resolution | [KeycodeService.cs](src/SvalboardLayerViz.Core/Keymap/KeycodeService.cs) |
| Keycode encode/decode | [KeycodeCodec.cs](src/SvalboardLayerViz.Core/Keymap/KeycodeCodec.cs) |
| Transparent key walk | [TransparentKeyResolver.cs](src/SvalboardLayerViz.Core/Keymap/TransparentKeyResolver.cs) |
| Layer colors | [LayerColorService.cs](src/SvalboardLayerViz.Core/Keymap/LayerColorService.cs) |
| Auto layer switch | [LayerSwitchService.cs](src/SvalboardLayerViz.Core/Keymap/LayerSwitchService.cs) |
| Edit session | [KeymapEditSession.cs](src/SvalboardLayerViz.Core/Keymap/KeymapEditSession.cs) |
| Save flow | [SaveFlow.cs](src/SvalboardLayerViz.Core/Keymap/SaveFlow.cs) |
| Pre-save safety | [PreSaveSafetyCheck.cs](src/SvalboardLayerViz.Core/Keymap/PreSaveSafetyCheck.cs) |
| Physical layout | [SvalboardLayout.cs](src/SvalboardLayerViz.Core/Layout/SvalboardLayout.cs) |
| Board layout computation | [BoardLayoutComputer.cs](src/SvalboardLayerViz.Core/Layout/BoardLayoutComputer.cs) |
| Export rendering | [BoardRenderer.cs](src/SvalboardLayerViz.Core/Export/BoardRenderer.cs) |
| Shared key styling | [KeyStyleResolver.cs](src/SvalboardLayerViz.Core/Export/KeyStyleResolver.cs) |
| Snapshots | [SnapshotService.cs](src/SvalboardLayerViz.Core/History/SnapshotService.cs) |
| Settings | [SettingsService.cs](src/SvalboardLayerViz.Core/Settings/SettingsService.cs) |
| Atomic file I/O | [AtomicFile.cs](src/SvalboardLayerViz.Core/Persistence/AtomicFile.cs) |
| Macro codec | [MacroCodec.cs](src/SvalboardLayerViz.Core/Macros/MacroCodec.cs) |
| Composition root | [App.axaml.cs](src/SvalboardLayerViz.App/App.axaml.cs) |
| Root VM | [MainWindowViewModel.cs](src/SvalboardLayerViz.App/ViewModels/MainWindowViewModel.cs) |
| Layer VM | [LayerViewModel.cs](src/SvalboardLayerViz.App/ViewModels/LayerViewModel.cs) |
| Hand VM | [HandViewModel.cs](src/SvalboardLayerViz.App/ViewModels/HandViewModel.cs) |
| Cluster VM | [KeyClusterViewModel.cs](src/SvalboardLayerViz.App/ViewModels/KeyClusterViewModel.cs) |
| Key VM | [KeyViewModel.cs](src/SvalboardLayerViz.App/ViewModels/KeyViewModel.cs) |
| Color palette | [LayerColorPalette.cs](src/SvalboardLayerViz.App/ViewModels/LayerColorPalette.cs) |
| Slot-list editor base | [SlotListEditorViewModel.cs](src/SvalboardLayerViz.App/ViewModels/SlotListEditorViewModel.cs) |
| Main window | [MainWindow.axaml](src/SvalboardLayerViz.App/Views/MainWindow.axaml) |
| Board view | [BoardView.axaml](src/SvalboardLayerViz.App/Views/BoardView.axaml) |
| Hand view | [HandView.axaml](src/SvalboardLayerViz.App/Views/HandView.axaml) |
| Thumb cluster (creative) | [ThumbClusterView.axaml](src/SvalboardLayerViz.App/Views/ThumbClusterView.axaml) |
| Key view | [KeyView.axaml](src/SvalboardLayerViz.App/Views/KeyView.axaml) |
| Design doc | [SvalboardLayerViz-DesignDoc.md](docs/SvalboardLayerViz-DesignDoc.md) |

---

## 12. Testing

~1188 tests in [src/SvalboardLayerViz.Tests/](src/SvalboardLayerViz.Tests/), organized by concern. Coverage density:

| Area | Depth | Notes |
|---|---|---|
| Keymap codecs / QMK settings / edit session | **Heavy** | 314+ tests. Codec round-trips, undo/redo, setting validation, transparent-key walk, color resolution. |
| Protocol command encoding | **Heavy** | 116+ tests. OOM caps, fsync atomicity, volatile-flag concurrency. No raw-byte fuzzing. |
| Snapshots | Solid | 50+ tests. Capture, diff, restore. Thin on multi-device and schema migration. |
| Layout | Medium | ~30 tests. Hand/cluster/key positioning, UI/export parity. Light on rendering. |
| Export | Medium | ~20 tests. PNG/PDF/SVG output, color/opacity logic. Thin on edge cases (huge layer counts, malformed device colors). |
| Dynamic entries (macros/combos/tap-dances) | Medium | ~60 tests. Encode/decode, text-byte collision handling. Thin on buffer overflow. |
| Dialog ViewModels | Medium | ~150 tests. PickerSession and MacroEditor are heavily covered; others sparser. |
| `MainWindowViewModel` lifecycle | **Thin** | **2 tests.** Only happy-path connect + failure-to-connect. Missing: mid-save disconnect, shutdown-mid-save, exception recovery, double-dispose. |
| Avalonia UI bindings / event dispatch | **Absent** | No UI test harness. Mitigated by manual smoke testing per session notes. |
| Settings / permissions | Thin | 12 tests. Round-trip + corrupt-file fallback. No file-lock or permission-error coverage. |
| Global hotkey | Absent | Platform-specific SharpHook integration not covered. |

The thin/absent rows are the highest-risk gaps. See §13 rethink item 2 for the most important one.

---

## 13. Rethink candidates

Honest list. Ranked by whether I'd actually spend time on it right now, not by severity. Each item has: what's wrong, why it matters, suggested fix, and my urgency read.

> **Status update — 2026-04-19.** The top four items (1, 2, 3, 7) — the "What I'd do next" list at the bottom of this section — landed in session [19-04-26](19-04-26.md) entries 360–364. Each affected item is annotated **DONE** below with a pointer to the implementing entry. Items 4, 5, 6, 8, 9, 10 are explicitly deferred per the same session.

### 1. `MainWindowViewModel` lifecycle has almost no test coverage — **urgent** — DONE (entry 364)

**What.** [MainWindowViewModelShowTests.cs](src/SvalboardLayerViz.Tests/ViewModels/MainWindowViewModelShowTests.cs) has 2 tests: "connects when device available" and "fails when no device." Missing: what happens if the device disconnects mid-save? What if `TryConnectAsync` throws halfway through? What if the user closes the window while a save is in flight? What if `Dispose` is called twice?

**Why it matters.** The VM is the highest-risk glue point in the codebase: UI thread vs background thread, HID stream cleanup, state consistency during edit-in-flight. Sessions 17-04-26 through 18-04-26-e hardened this code (volatile flags, interlocked gates, async shutdown) but the tests never caught up. A future regression lands here and nothing notices.

**Fix.** Add at minimum:
- `TryConnectAsync_DeviceDisconnectsDuringKeymapLoad_DoesNotCrash` — simulate cable yank mid-load.
- `Shutdown_WhileSaveInFlight_WaitsForSaveToComplete_OrTimesOutGracefully`.
- `Show_CorruptSettingsFile_FallsBackToDefaults`.
- `TryConnectAsync_HidSharpThrows_StatusMessageReflectsFailure`.

These are integration-flavor tests (VM + fake protocol + fake device connection) but don't need an Avalonia harness. An `IVialProtocolService` fake that throws/delays at specific points is enough.

**Urgency.** High. Fix before the next release. The hostile reviews spotted real bugs here — the next time one slips through will be from a test gap.

**Implemented (entry 364).** [MainWindowViewModelLifecycleTests.cs](src/SvalboardLayerViz.Tests/ViewModels/MainWindowViewModelLifecycleTests.cs) — five lifecycle tests using a new [FakeDeviceConnectionService](src/SvalboardLayerViz.Tests/Device/FakeDeviceConnectionService.cs): persisted-settings application, corrupt-settings fallback, no-device path, HID-enumeration-throws path, and the double-`ShutdownAsync` Interlocked-gate test. The `Shutdown_WhileSaveInFlight` and `DisconnectsDuringKeymapLoad` cases from the original list still need a fake `DeviceSession` and were deferred. `TryConnectAsync` was widened from `private` to `internal` as a test seam.

### 2. `KeycodeService` is stateful and requires implicit setup — **medium** — DONE (entry 361)

**What.** [KeycodeService.cs](src/SvalboardLayerViz.Core/Keymap/KeycodeService.cs) has three setters: `SetCustomKeycodes`, `SetMacroPreviews`, `SetCustomKeyLabels`. They must be called before `Resolve` or custom keycodes silently fall back to hex output. No compile-time check. A caller who forgets gets a silent bug.

**Why it matters.** The App layer does call these in the right order today, so production is fine. But it's fragile — the setup contract is implicit ceremony, not enforced. A future refactor that splits the setup or adds a new consumer might miss a setter. The tests don't catch the omission because they set everything up explicitly.

**Fix.** Two options:
- **Factory method** — `KeycodeService.ForConfig(KeyboardConfig)` returns a pre-configured instance. Caller can't get it wrong.
- **Constructor injection** — pass all three collections to the constructor. Immutable after creation. If a collection changes, replace the service.

Prefer the factory method. It keeps the VM's existing usage pattern (one long-lived service) while making setup a single call.

**Urgency.** Medium. Not breaking anything today. Worth cleaning up before someone adds a fourth setter.

**Implemented (entry 361).** Constructor on [KeycodeService.cs](src/SvalboardLayerViz.Core/Keymap/KeycodeService.cs) now accepts all three collections as optional params (`customKeycodes`, `customKeyLabels`, `macroPreviews`), so a fully-configured instance is one call. Setters retained for runtime updates (deferred macro load, settings-dialog edits) — those genuinely need to mutate the live service rather than rebuild it.

### 3. Partial DI in `MainWindowViewModel` — **medium** — DONE (entry 362)

**What.** [MainWindowViewModel.cs:34–38](src/SvalboardLayerViz.App/ViewModels/MainWindowViewModel.cs#L34) declares five service fields. Three are injected via constructor (`IVialProtocolService`, `ISettingsService`, `ISnapshotService`). Two are hardcoded inline (`DeviceConnectionService`, `KeycodeService`). This is inconsistent — either all services should be injectable for testability, or none of them should be (drop the interfaces).

**Why it matters.** Makes testing harder. If a test wants a fake `DeviceConnectionService` (to simulate specific enumeration orders or hotplug sequences), it can't — the field is a concrete `new()`. The interface-based half is also a minor waste: `ISettingsService` has one implementation and no test uses a fake. You're paying interface-dispatch cost and allocation complexity for no realized benefit.

**Fix.** Decide which side to land on:
- **More DI** — Extract `IDeviceConnectionService` and `IKeycodeService`, pass them through the constructor, update tests. This unlocks item 1's integration tests.
- **Less DI** — Drop `ISettingsService` and `ISnapshotService` interfaces; use concrete types. Keep `IVialProtocolService` as an interface because swapping the device for tests is genuinely valuable.

The "more DI" path is the one that pays off if you act on item 1's test coverage gap.

**Urgency.** Medium. Do it as part of the item 1 fix.

**Implemented (entry 362).** New [IDeviceConnectionService](src/SvalboardLayerViz.Core/Device/IDeviceConnectionService.cs) interface; concrete `DeviceConnectionService` implements it; `DeviceSession.Connect` takes the interface. `MainWindowViewModel` constructor now accepts `IDeviceConnectionService?` and `KeycodeService?` as optional params (appended at the end so positional callers still compile). `KeycodeService` deliberately not interfaced — pure translator with no I/O, no test value in faking it.

### 4. `KeymapEditSession` has mirrored forward/reverse switches — **low**

**What.** [KeymapEditSession.cs:342–396](src/SvalboardLayerViz.Core/Keymap/KeymapEditSession.cs#L342) has two methods, `ExecuteForward` and `ExecuteReverse`, that are mirror-image switch statements over `EditOp` subtypes. Every new op type requires updates in two places. Every bounds-check fix must be duplicated. Session 18-04-26-e added `ValidateSettingId` to both branches — confirmed drift risk.

**Why it matters.** Today, the EditOp family has 5 variants and tests catch drift. Tomorrow, if you add a 6th variant and forget the reverse case, undo silently does the wrong thing and tests might miss it if they only exercise forward.

**Fix.** Two options:
- **Single method** — `Execute(op, isForward)` with ternaries like `isForward ? op.NewCode : op.OldCode`.
- **Visitor on `EditOp`** — `op.Execute(this, isForward)` dispatches polymorphically. More OO, requires touching every EditOp subtype.

Option 1 is less disruptive. Option 2 is more extensible if the EditOp family keeps growing.

**Urgency.** Low. Not breaking anything. Do it opportunistically when next adding an EditOp subtype.

### 5. `App.axaml.cs` owns too much — **low**

**What.** 813 lines. Handles exception wiring, settings load, VM creation, window geometry, tray icon menu, global hotkey, ~18 dialog factories, shutdown coordination. Several concerns that could live elsewhere.

**Why it matters.** File size alone is okay (well-organized internally). But it's the single most load-bearing file in the App project and any refactor touches it. If multi-window support or plugin architecture lands, this file becomes unmaintainable.

**Fix.** Only if scope grows. Extract:
- `CompositionRoot` — service instantiation and VM wiring.
- `DialogFactories` — the ~18 dialog-opener methods. Some of this already lives in [EditorDialogFactory](src/SvalboardLayerViz.App/Dialogs/EditorDialogFactory.cs); extend the pattern.
- `ShutdownCoordinator` — async shutdown sequence.

Keep `App.axaml.cs` as the Avalonia entry point and lifecycle handler.

**Urgency.** Low. Pragmatic as-is for a single-window app. Revisit if you add a second window or a plugin system.

### 6. `VialProtocolService` couples transport and protocol — **low**

**What.** 764 lines in one file. Owns USB HID transport (lock, reports, platform quirks) and Vial protocol encoding (40+ command methods, payload parsing, XZ magic-byte scan). Platform-specific HID changes and protocol changes both land in the same place.

**Why it matters.** If a new firmware version ships a Vial v2 command set, or macOS changes HID report ID handling, you touch the same class. Harder to test transport in isolation from protocol.

**Fix.** Extract `IHidTransport` (Send 32 bytes, receive 32 bytes, platform quirks). `VialProtocolService` depends on it. Allows swapping to WebUSB, libusb, or native platform HID without rewriting the protocol layer.

**Urgency.** Low. Only worth doing if a second transport backend is actually needed (e.g., supporting Svalboard over Bluetooth, or running in a browser via WebHID). Keep an eye on this if those become real.

### 7. Settings loaded 3× at startup — **trivial** — DONE (entry 363)

**What.** [App.axaml.cs:46](src/SvalboardLayerViz.App/App.axaml.cs#L46), [App.axaml.cs:70](src/SvalboardLayerViz.App/App.axaml.cs#L70), and inside `MainWindowViewModel` constructor each call `SettingsService.Load()`. Three file reads on startup for ~1 KB of JSON.

**Why it matters.** Performance cost is nil (<1 ms on any platform). But it's inelegant and hints at unclear ownership — who owns the loaded settings object? If someone later adds write-then-read logic, the three loads could produce inconsistent views.

**Fix.** Load once at App startup, pass the `AppSettings` object through to `MainWindowViewModel` constructor, and let the VM hold it (or re-ask `SettingsService` only when changes need persisting).

**Urgency.** Trivial. Five-minute cleanup.

**Implemented (entry 363).** Three of the four `settingsService.Load()` calls in `App.axaml.cs` now reuse the `initialSettings` instance loaded once at startup. Three loads intentionally kept: `SaveWindowState` (read-before-write needs fresh data), the export-flow load (colors may have been edited mid-session), and the help-window-close save (avoid clobbering concurrent edits).

### 8. `MacroCodec` text-byte `0x01` edge case — **document only**

**What.** [MacroCodec.cs](src/SvalboardLayerViz.Core/Macros/MacroCodec.cs) byte value `0x01` has dual meaning: action prefix (followed by subcommand) or literal ASCII SOH character. Encoder assumes text is printable ASCII; a macro containing literal `0x01` in send-string text won't round-trip correctly.

**Why it matters.** Practically never happens in real macros. QMK's `SS_SEND_STRING` doesn't allow control characters. But if a power user crafts one manually or firmware behavior changes, silent data corruption.

**Fix.** Short-term: document in `MacroCodec` docstring. Long-term: if QMK allows user-provided control characters, add an escape path (`0x01` → `[0x01, 0x00]` or similar).

**Urgency.** Document-only. Actual code fix not needed unless someone hits it.

### 9. Localization designer is hand-maintained — **low**

**What.** [Strings.Designer.cs](src/SvalboardLayerViz.App/Resources/Strings.Designer.cs) is manually edited. Adding a string requires three synchronized edits (en + nl + designer). A missing translation in `Strings.nl.resx` falls back to English silently; no test verifies resource completeness.

**Why it matters.** At 110 keys it's manageable. At 200+ it becomes a chore and silent drift becomes likely.

**Fix.** MSBuild task to auto-generate the designer from `Strings.resx`. Add a test that asserts `Strings.resx` and `Strings.nl.resx` have identical key sets.

**Urgency.** Low. Revisit when you add a third language or cross 200 keys.

### 10. `BoardRenderer` baked to Svalboard specifics — **only if multi-keyboard is on the roadmap**

**What.** [BoardRenderer.cs](src/SvalboardLayerViz.Core/Export/BoardRenderer.cs) (~510 lines) knows about Svalboard thumb trapezoid shapes and cluster grouping. Second keyboard = substantial rewrite or abstraction.

**Why it matters.** Today, only one keyboard is supported. Not a real concern. If the project expands to other Vial keyboards, this is where the friction will show up first.

**Fix.** Only if needed. `IKeyRenderer` interface with Skia-backed and hypothetical other-format-backed implementations.

**Urgency.** None unless multi-keyboard becomes a real goal.

---

### What I'd do next, in order

1. ~~**Add the 4 lifecycle tests from item 1.** This is the highest-value test debt.~~ Done (entry 364).
2. ~~**Make `DeviceConnectionService` + `KeycodeService` injectable (item 3) as part of that test work.** Lands the cleanup while you're in the file.~~ Done (entry 362).
3. ~~**Turn `KeycodeService` setters into a factory method (item 2).** Small, isolated, removes a silent-failure mode.~~ Done (entry 361).
4. ~~**Collapse the three startup settings loads (item 7).** Trivial cleanup.~~ Done (entry 363).
5. **Everything else — defer.** Items 4, 5, 6, 8, 9, 10 are all fine as-is for the current scope. Revisit only if scope changes.

The remaining open work surfaced while doing items 1–4: the two more invasive lifecycle tests (`Shutdown_WhileSaveInFlight_WaitsForSaveToComplete` and `TryConnectAsync_DeviceDisconnectsDuringKeymapLoad`) need a fakeable `DeviceSession`, which doesn't exist yet. Pick those up next time the lifecycle code is touched.

---

*Last updated: 2026-04-19. Reflects code state at branch `experimental-stuff` after session 19-04-26.*

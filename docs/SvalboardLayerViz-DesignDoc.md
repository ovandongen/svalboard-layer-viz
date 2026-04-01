# SvalboardLayerViz — Design Document

**Author:** Olaf van Dongen
**Date:** April 2026
**Status:** Phase 1.5 Complete

---

## 1. Vision

A lightweight, cross-platform system tray application that connects to a Svalboard keyboard over USB, reads its full keymap configuration, and displays all layers in an interactive visual layout. The app stays out of the way until needed — a quick glance at the tray, a hotkey, or a click reveals exactly what every key does on every layer.

### What This Is Not (Yet)

This is not a configuration editor. Phase 1 is read-only: connect, read, visualize. Editing stays in Keybard/Keybard-ng/Vial where it belongs.

---

## 2. Problem Statement

The Svalboard is a radically different input device. Learning it means memorizing dozens of keys across multiple layers — and the current tools don't help much:

- **Keybard-ng** is a browser-based configuration tool, not an always-visible reference
- **Vial GUI** is a generic QMK editor, not tailored to Svalboard's unique layout
- **Printed layouts** (from Python scripts) are static and go stale after every config change
- **No tool** currently shows which layer is active in real-time

### Target Users

- Svalboard owners learning the device (primary)
- Experienced users who customize frequently and lose track of layers
- The Svalboard open-source community (potential contribution)

---

## 3. Tech Stack

| Component | Technology | Rationale |
|-----------|-----------|-----------|
| **UI Framework** | Avalonia UI (.NET) | Cross-platform XAML, system tray support, WPF-like experience |
| **Language** | C# / .NET 10 | Developer's primary skill, cross-platform runtime |
| **USB HID** | HidSharp | Cross-platform USB HID library for .NET, well-maintained |
| **Data Format** | XZ decompression → JSON | Vial protocol delivers keyboard definitions as XZ-compressed JSON |
| **Build** | dotnet CLI + standard tooling | Simple, no exotic build pipeline |

### Why Avalonia Over Alternatives

- **vs WPF:** WPF is Windows-only. Avalonia runs on Windows, macOS, Linux.
- **vs MAUI:** MAUI has no Linux support and isn't designed for system tray apps.
- **vs Electron/Tauri:** Heavier, and doesn't leverage existing .NET/XAML skills.
- **vs Web (React):** Can't do system tray. Could be a component later, but not the shell.

---

## 4. Architecture Overview

```
┌──────────────────────────────────────────────┐
│                 Avalonia UI                    │
│  ┌──────────┐  ┌───────────┐  ┌───────────┐  │
│  │  System   │  │  Layer    │  │  Settings  │  │
│  │  Tray     │  │  Viewer   │  │  Panel     │  │
│  └────┬─────┘  └─────┬─────┘  └───────────┘  │
│       │               │                        │
│  ┌────┴───────────────┴────────────────────┐  │
│  │          ViewModel Layer                 │  │
│  │   KeyboardViewModel · LayerViewModel     │  │
│  └──────────────────┬──────────────────────┘  │
│                     │                          │
│  ┌──────────────────┴──────────────────────┐  │
│  │          Service Layer                   │  │
│  │  VialProtocolService · KeycodeService    │  │
│  │  DeviceConnectionService                 │  │
│  └──────────────────┬──────────────────────┘  │
│                     │                          │
│  ┌──────────────────┴──────────────────────┐  │
│  │         HID Transport (HidSharp)         │  │
│  └──────────────────────────────────────────┘  │
└──────────────────────────────────────────────┘
         │
    USB HID (32-byte reports)
         │
   ┌─────┴─────┐
   │ Svalboard  │
   └───────────┘
```

### Layer Separation

- **Transport:** Raw USB HID communication (HidSharp). Sends/receives 32-byte buffers.
- **Protocol:** Vial command encoding/decoding. Translates between C# objects and byte arrays.
- **Service:** Orchestrates loading a full keyboard config (layer count → keymap → definition → decode).
- **ViewModel:** Exposes keyboard state to the UI. Handles layer selection, search, etc.
- **View:** XAML-based rendering of the board, keys, and layers.

---

## 5. Vial Protocol Reference

All communication uses 32-byte USB HID reports. The host sends a command; the device responds with a 32-byte report.

### 5.1 Device Identification

Vial keyboards expose an auxiliary HID interface with:
- **Usage Page:** `0xFF60`
- **Usage:** `0x61` or `0x62`

HidSharp can filter on these to find Vial-compatible devices.

### 5.2 Core VIA Commands

| ID | Command | Description |
|----|---------|-------------|
| `0x01` | GET_PROTOCOL_VERSION | Returns VIA protocol version |
| `0x04` | GET_KEYCODE | Get keycode at (layer, row, col) |
| `0x05` | SET_KEYCODE | Set keycode at (layer, row, col) |
| `0x11` | GET_LAYER_COUNT | Returns number of layers |
| `0x12` | KEYMAP_GET_BUFFER | Read keymap data in 28-byte chunks |

### 5.3 Vial Commands (prefixed with `0xFE`)

| Sub-ID | Command | Description |
|--------|---------|-------------|
| `0x00` | GET_KEYBOARD_ID | Unique keyboard identifier |
| `0x01` | GET_SIZE | Size of compressed definition payload |
| `0x02` | GET_DEFINITION | Fetch definition in 28-byte blocks |
| `0x05` | GET_UNLOCK_STATUS | Check if keyboard is unlocked for writes |

### 5.4 Loading the Full Configuration

The sequence to load everything:

```
1. Open HID device (usagePage 0xFF60)
2. GET_KEYBOARD_ID        → identify the board
3. GET_PROTOCOL_VERSION    → confirm compatibility
4. GET_LAYER_COUNT         → e.g., returns 8
5. GET_SIZE                → size of definition payload
6. GET_DEFINITION (loop)   → fetch compressed JSON in blocks
7. Decompress XZ           → parse JSON (matrix dims, layout, custom keycodes)
8. KEYMAP_GET_BUFFER (loop) → fetch full keymap (layers × rows × cols × 2 bytes)
9. Map keymap values to keycode names using key.service logic
```

### 5.5 Keymap Data Format

- Each key is a **16-bit unsigned integer** in **big-endian** byte order
- Matrix is **10 rows × 6 columns** for Svalboard
- Total keymap: `layers × 10 × 6 × 2` bytes
- Fetched in 28-byte chunks via KEYMAP_GET_BUFFER

### 5.6 Keycode Encoding

Keycodes are 16-bit values (post-2023 QMK layout, matching keybard-ng `keygen.ts`):

- **No key / Transparent:** `0x0000` (KC_NO), `0x0001` (KC_TRNS — falls through to layer below)
- **Basic keys:** `0x0004–0x00FF` (KC_A = `0x04`, KC_SPACE = `0x2C`, mouse keys `0xCD–0xDC`, modifiers `0xE0–0xE7`)
- **Modifier + key:** `0x0100–0x1FFF` — `(mods << 8) | keycode` where mods is a bitmask (Ctrl=0x01, Shift=0x02, Alt=0x04, GUI=0x08). Shift-only + symbol key resolves to the shifted symbol (e.g., `{` instead of `Shift [`)
- **Mod-tap (MT):** `0x2000–0x3FFF` — hold = modifier, tap = keycode
- **Layer-tap (LT):** `0x4000–0x4FFF` — hold = layer, tap = keycode. `LT(n, KC_NO)` shows as `LT(n)`
- **Layer-mod (LM):** `0x5000–0x51FF` — activate layer with modifier
- **Layer functions:** `0x5200–0x52DF` (32 slots each):
  - `TO` (0x5200) — Switch permanently
  - `MO` (0x5220) — Momentary (active while held)
  - `DF` (0x5240) — Set default layer
  - `TG` (0x5260) — Toggle on/off
  - `OSL` (0x5280) — One-shot layer
  - `OSM` (0x52A0) — One-shot modifier
  - `TT` (0x52C0) — Tap-toggle
- **Custom keycodes:** `0x7E00–0x7FFF` (QK_KB + QK_USER) — keyboard-specific and user keycodes from the Vial definition JSON. Svalboard has 20 (DPI controls, scroll toggles, sniper modes, etc.)

### 5.7 Definition Payload

The compressed JSON payload contains:

```json
{
  "matrix": { "rows": 10, "cols": 6 },
  "customKeycodes": [
    { "name": "...", "title": "...", "shortName": "..." }
  ],
  "layouts": { ... },
  "lighting": "..."
}
```

The definition is XZ-compressed. Check for magic bytes `0xFD 0x37 0x7A 0x58 0x5A 0x00`. The device may echo the command prefix (2 bytes), so detect offset by scanning for the magic bytes.

---

## 6. Svalboard Physical Layout

### 6.1 Key Count and Arrangement

The Svalboard's layout is fundamentally different from a traditional keyboard. Rather than pressing keys downward, each finger sits in a well and can push in five directions.

- **60 total keys** across the full board (10 clusters × 6 positions in the matrix, with the matrix organized as 10 rows × 6 columns)
- Split into **left hand** and **right hand**
- Each hand has:
  - **4 finger clusters** (index, middle, ring, pinky) — each with **5 keys per finger**:
    - **North** — push the key away from you
    - **South** — pull the key toward you
    - **East** — push the key to the right
    - **West** — push the key to the left
    - **Down** — press the key straight down (like a traditional keypress)
  - **Thumb cluster** (3–4 keys per thumb, including layer-switching keys like MO(2), MO(4), MO(5))
  - **Modifier keys** (Ctrl, Shift, Tab/Esc, etc.) positioned on the inner edge

### 6.2 Cluster Layout Diagram

From the Skim project's rendering and Keybard-ng's `svalboard-layout.ts`:

```
             Left Hand                              Right Hand
    Pinky   Ring  Middle  Index          Index  Middle  Ring   Pinky
    ┌───┐  ┌───┐  ┌───┐  ┌───┐          ┌───┐  ┌───┐  ┌───┐  ┌───┐
    │ N │  │ N │  │ N │  │ N │          │ N │  │ N │  │ N │  │ N │
  ┌─┼───┼─┐┼───┼─┐┼───┼─┐┼───┼─┐    ┌─┼───┼─┐┼───┼─┐┼───┼─┐┼───┼─┐
  │W│ D │E││W│ D │E││W│ D │E││W│ D │E│  │W│ D │E││W│ D │E││W│ D │E││W│ D │E│
  └─┼───┼─┘┼───┼─┘┼───┼─┘┼───┼─┘    └─┼───┼─┘┼───┼─┘┼───┼─┘┼───┼─┘
    │ S │  │ S │  │ S │  │ S │          │ S │  │ S │  │ S │  │ S │
    └───┘  └───┘  └───┘  └───┘          └───┘  └───┘  └───┘  └───┘
                  ┌─────────┐                  ┌─────────┐
                  │  Thumb  │                  │  Thumb  │
                  │ cluster │                  │ cluster │
                  └─────────┘                  └─────────┘

  N = North, S = South, E = East, W = West, D = Down (center)
```

### 6.3 Coordinate System

From Keybard-ng's layout definition (`svalboard-layout.ts`) and Skim's rendering:

- Keys are positioned on a coordinate grid with X range ~0–23, Y range ~0–6
- Each key occupies 1×1 unit (standard key size)
- 1 unit = 60 pixels in Keybard-ng's rendering
- Clusters are arranged in staggered columns matching ergonomic finger positions
- Skim uses a similar coordinate approach but renders via Typst templates with JSON data injection

### 6.4 Visual Design Considerations

The five-direction-per-finger layout is the core challenge and opportunity for visualization. The rendering needs to:

- Clearly show the **left/right hand split**
- Group keys into **finger clusters** with the directional arrangement (N/S/E/W/Down) visually obvious — the cross/plus pattern per finger is the natural representation
- Distinguish the **down** key (center of each cluster) from directional keys
- Show **modifier** and **thumb clusters** distinctly
- Use **color coding per layer** (Svalboard stores per-layer RGB colors as hue/sat/val)
- Adopt **HLS color space** for layer gradients (as Skim does — perceptually smoother than raw RGB)
- Indicate **layer-switching keys** with color coding showing which layer they activate (Skim does this well)

---

## 7. Phased Delivery

### Phase 1: Read and Display (MVP) — COMPLETE

**Goal:** Connect to Svalboard, read full config, display all layers interactively.

Implemented features:
- [x] System tray icon with Show/Toggle/Quit menu
- [x] Auto-detect Svalboard on USB connect/disconnect (HidSharp device monitoring)
- [x] Read full keymap and definition from device (Vial protocol, XZ decompression)
- [x] Visual board layout showing all keys with labels, positioned per physical layout
- [x] Layer tabs with WrapPanel for responsive layout, empty layers hidden
- [x] Transparent keys resolved by walking the layer stack, shown dimmed
- [x] Algorithmic color coding per layer (HLS color space, evenly-spaced hues)
- [x] Global hotkey (F12) to show/hide overlay via SharpHook
- [x] Comprehensive keycode resolution: basic keys, modifiers, layer functions (MO/TG/DF/TO/TT/OSL/OSM), mod-tap (MT), layer-tap (LT), layer-mod (LM), shifted symbols, mouse keys, custom/keyboard-specific keycodes (QK_KB range)
- [x] Responsive scaling via Viewbox
- [x] 160 unit tests

Scope explicitly excluded:
- No config editing
- No auto layer detection

### Phase 1.5: Polish & Settings — COMPLETE

**Goal:** User-configurable settings and visual polish.

Implemented features:
- [x] Settings persistence — JSON settings file at `{ApplicationData}/SvalboardLayerViz/settings.json`, defaults on missing/corrupt
- [x] Settings window — accessible via gear icon button or tray menu
- [x] Layer color configuration — ColorView picker per layer, user-picked colors render faithfully (not darkened)
- [x] Configurable global hotkey — key + modifier combo (Ctrl/Shift/Alt/GUI checkboxes)
- [x] Layer names — user-defined, stored locally, displayed in layer tabs
- [x] Layer-switch key coloring — MO/TG/LT/DF/TO/TT/OSL keys colored to match their target layer
- [x] Custom key labels — right-click any key to assign a label, or manage in settings page. User labels override all keycode resolution. Labels persisted for reuse in layout printing (Phase 3)
- [x] Transparent overlay window — no system decorations, fully transparent background, desktop shows through
- [x] Auto-flipping bars — status bar + layer tabs auto-switch top/bottom based on window screen position; status bar always at outer edge
- [x] Icon buttons — gear (settings) and refresh icons replace text
- [x] Auto-contrast text — W3C luminance-based white/dark text on keys
- [x] Window dragging — custom drag via bars area (replaces missing title bar)
- [x] 165 unit tests

### Phase 2: Live Layer Detection (NEXT)

**Goal:** Show which layer is currently active in real-time.

This requires firmware support. Options:
1. **Raw HID query** — add a custom command to Svalboard firmware that reports `layer_state`. QMK supports this via `raw_hid_receive()`/`raw_hid_send()`. Would need a PR to the Svalboard firmware repo.
2. **Macro-based detection** — bind layer toggle keys to also emit a hidden keycode (like F24). The app catches these globally. Simpler but only works for toggle layers, not momentary.
3. **Community collaboration** — propose the Raw HID approach to the Svalboard community. Since it's open source and actively maintained, this seems feasible.

### Phase 3: Enhanced Visualization

- Export to image/PDF for printing

## 8. Project Structure

```
SvalboardLayerViz/
├── SvalboardLayerViz.sln
├── src/
│   ├── SvalboardLayerViz.App/              # Avalonia application
│   │   ├── App.axaml(.cs)                  # Application entry, tray menu, hotkey wiring, settings/label dialogs
│   │   ├── Views/
│   │   │   ├── MainWindow.axaml(.cs)       # Transparent overlay window, auto-flipping bars, custom drag
│   │   │   ├── BoardView.axaml             # Full board visualization (Viewbox + Canvas)
│   │   │   ├── KeyView.axaml               # Single key visual (UserControl, right-click context menu)
│   │   │   └── SettingsWindow.axaml(.cs)   # Settings UI (colors, names, labels, hotkey)
│   │   ├── ViewModels/
│   │   │   ├── MainWindowViewModel.cs      # Root state, device lifecycle, layer selection, ApplySettings
│   │   │   ├── KeyViewModel.cs             # Per-key display: positioning, colors, tooltips, set-label command
│   │   │   ├── LayerViewModel.cs           # Per-layer: keys collection, tab color
│   │   │   ├── ClusterViewModel.cs         # Cluster background bounding boxes
│   │   │   └── SettingsViewModel.cs        # Settings page: layer settings, custom labels, hotkey
│   │   ├── Converters/
│   │   │   └── HexColorToBrushConverter.cs # Hex string → SolidColorBrush for live preview
│   │   └── Services/
│   │       └── GlobalHotkeyService.cs      # SharpHook-based global hotkey listener
│   │
│   ├── SvalboardLayerViz.Core/             # Business logic (no UI dependency)
│   │   ├── Protocol/
│   │   │   ├── VialCommands.cs             # Command ID constants
│   │   │   ├── IVialProtocolService.cs     # Protocol interface (for testability)
│   │   │   ├── VialProtocolService.cs      # Encode/decode Vial HID messages
│   │   │   └── XzDecompressor.cs           # XZ decompression for definitions
│   │   ├── Device/
│   │   │   ├── DeviceConnectionService.cs  # HidSharp wrapper, connect/disconnect
│   │   │   └── DeviceInfo.cs               # Device metadata
│   │   ├── Settings/
│   │   │   ├── UserSettings.cs             # Settings record (colors, names, labels, hotkey)
│   │   │   ├── ISettingsService.cs         # Load/Save interface
│   │   │   └── SettingsService.cs          # JSON persistence at {AppData}/SvalboardLayerViz/settings.json
│   │   ├── Keymap/
│   │   │   ├── KeycodeService.cs           # Translate 16-bit codes to labels (user labels checked first)
│   │   │   ├── KeymapLoader.cs             # Orchestrate full config load
│   │   │   ├── TransparentKeyResolver.cs   # Walk layer stack for KC_TRNS
│   │   │   └── LayerColorService.cs        # HLS-based color gen, user override, auto-contrast text
│   │   ├── Layout/
│   │   │   ├── SvalboardLayout.cs          # Physical key positions (52 keys)
│   │   │   └── LayoutDefinition.cs         # Parsed definition from device
│   │   └── Models/
│   │       ├── KeyboardConfig.cs           # Full loaded config
│   │       ├── Layer.cs                    # Single layer's keys + color hints
│   │       ├── Key.cs                      # Key position + keycode + labels + IsLayerSwitch/IsUnknown
│   │       └── CustomKeycode.cs            # Custom keycode from device definition
│   │
│   └── SvalboardLayerViz.Tests/            # Unit tests (165 tests)
│       ├── Keymap/
│       │   ├── KeycodeServiceTests.cs      # Keycode resolution (all ranges)
│       │   ├── TransparentKeyResolverTests.cs
│       │   └── LayerColorServiceTests.cs
│       ├── Layout/
│       │   └── SvalboardLayoutTests.cs
│       ├── Settings/
│       │   └── SettingsServiceTests.cs     # Round-trip, defaults, corrupt file fallback
│       └── ViewModels/
│           ├── KeyViewModelTests.cs
│           ├── ClusterViewModelTests.cs
│           └── MainWindowViewModelShowTests.cs
│
├── docs/
│   ├── SvalboardLayerViz-DesignDoc.md      # This document
│   └── 01-04-26.md                         # Change log
└── README.md
```

### Key Design Decisions

**Separation of Core and App:** The `Core` project has zero UI dependencies. This means:
- Protocol and keymap logic can be unit tested without Avalonia
- Could later reuse Core for a CLI tool, web app, or other UI
- Easier to reason about and test

**MVVM Pattern:** Standard for Avalonia/WPF. ViewModels expose observable properties; Views bind to them. No code-behind for logic.

**Layout as Data:** The physical key positions are defined as data (matching Keybard-ng's `svalboard-layout.ts`), not hard-coded in XAML. This makes it straightforward to add other boards later.

---

## 9. Key Visual Component Design

The heart of the UI is the board visualization. Here's the approach:

### Single Key (`KeyView`)

Each key is a small XAML UserControl:
- Background color based on current layer's color theme
- Primary label (the key's function, e.g., "A", "Ctrl", "MO(2)")
- Secondary label for modified keys (e.g., "Shift+A" shows "A" primary, "Shift" secondary)
- Transparency indicator — if the key is KC_TRNS, show the effective key from the layer below with a visual hint (dimmed or dashed border)
- Hover tooltip with full keycode details

### Board Layout (`BoardView`)

A Canvas or ItemsControl that positions KeyView instances according to the physical layout coordinates:
- Each key placed at (x × scale, y × scale) from the layout data
- Left/right hand groups with clear visual separation
- Finger clusters visually grouped (subtle background or spacing)
- Thumb clusters positioned below the main keys

### Layer Navigation

- Tab strip or segmented control along the top
- Each tab labeled with layer number and optional name
- Color indicator per layer matching the board's layer_colors
- Keyboard shortcuts (1–8) to jump between layers

---

## 10. USB Connection Lifecycle

```
App Start
  │
  ├─ Enumerate HID devices (HidSharp)
  │   Filter: usagePage=0xFF60, usage=0x61/0x62
  │
  ├─ Found? → Connect → Load config → Display
  │
  └─ Not found? → Show "Connect your Svalboard" in tray
       │
       └─ Monitor for device connect events
            │
            └─ Device connected → Load config → Display

Device disconnected → Clear display → Return to monitoring
```

### Error Handling

- Device busy (another app has it open) → Clear message, suggest closing Vial/Keybard
- Read failure mid-transfer → Retry up to 3 times, then show error
- Unrecognized device → Show device name, suggest it might not be Svalboard

---

## 11. Data Flow: From Device to Pixels

```
USB HID bytes
    │
    ▼
VialProtocolService.GetLayerCount()     → int layerCount
VialProtocolService.GetDefinition()     → byte[] xzData
XzDecompressor.Decompress(xzData)       → string json
JsonSerializer.Deserialize(json)        → LayoutDefinition
VialProtocolService.GetKeymapBuffer()   → ushort[layers][rows][cols]
    │
    ▼
KeymapLoader.Load()                     → KeyboardConfig
    │                                      (layers, physical layout,
    │                                       resolved keycodes)
    ▼
BoardViewModel
    │  - exposes Layers[]
    │  - exposes SelectedLayer
    │  - resolves KC_TRNS to effective key
    ▼
BoardView.axaml
    │  - ItemsControl with Canvas panel
    │  - Each key: KeyView positioned by layout coords
    ▼
Screen
```

---

## 12. Open Questions & Resolved Decisions

### Resolved

1. **XZ decompression in .NET** — SharpCompress handles Vial's XZ variant correctly. Need to detect the magic byte offset (device echoes command prefix before XZ payload).

2. **HidSharp device filtering** — Filtering by UsagePage requires iterating `DeviceList` and checking `Indexes.ContainsValue()` with packed 32-bit HID usage values (`(usagePage << 16) | usageId`).

3. **Svalboard-specific extensions** — Custom keycodes are in the `QK_KB` range (0x7E00+), defined in the device's Vial definition JSON. The Svalboard has 20 custom keycodes (DPI controls, scroll toggles, sniper modes, etc.). Layer colors are stored as HSV in firmware but not yet exposed via protocol — using algorithmic HLS generation for now.

4. **Config change detection** — Manual refresh button implemented. The Refresh command re-reads the full config from the device.

5. **System tray behavior** — Avalonia's `TrayIcon` API works on macOS. Global hotkey via SharpHook (F12) provides show/hide toggle. macOS requires Accessibility permission for global key hooks.

6. **Custom keycode base address** — Vial maps `customKeycodes[]` starting at `QK_KB` (0x7E00), NOT `QK_USER` (0x7E40). Both ranges are covered by a single handler.

7. **Layer names** — Resolved in Phase 1.5. Users can name layers in the settings page; names are stored locally in `settings.json`.

8. **Layer color configuration** — Resolved in Phase 1.5. Users pick colors per layer via ColorView in settings. User-picked colors render faithfully; algorithmic colors used as defaults.

9. **Global hotkey configuration** — Resolved in Phase 1.5. Key + modifier combo (Ctrl/Shift/Alt/GUI) configurable in settings.

10. **Custom key labels** — Resolved in Phase 1.5. Right-click any key to assign a custom label, or manage in settings page. User labels override all keycode resolution (checked first after transparent/empty). Persisted in settings for reuse.

### Still Open

11. **Multiple Svalboards** — Handle the case where someone has more than one connected? Currently connects to the first found device.

12. **Ctrl modifier on macOS** — Global hotkey Ctrl+F12 didn't work on macOS (EventMask.Ctrl not matching). Now configurable so users can pick a working combo.

---

## 13. Dependencies

| Package | Purpose | License |
|---------|---------|---------|
| Avalonia | UI framework | MIT |
| Avalonia.Desktop | Desktop platform support | MIT |
| Avalonia.Controls.ColorPicker | Color picker for settings UI | MIT |
| HidSharp | USB HID communication | Apache 2.0 |
| SharpCompress | XZ decompression | MIT |
| CommunityToolkit.Mvvm | MVVM helpers (ObservableObject, RelayCommand) | MIT |
| SharpHook | Cross-platform global keyboard hooks (libuiohook) | MIT |
| System.Text.Json | JSON parsing (built-in) | MIT |

---

## 14. Prior Art: Skim (Svalboard Keymap Image Maker)

[Skim](https://github.com/Townk/skim) is a Python CLI tool by a Svalboard community member that generates professional SVG/PNG layout images from Svalboard config files. While it's a batch tool (not interactive), it solves many of the same problems and is a valuable reference for our implementation.

### What Skim Gets Right

- **Three config format parsers:** Keybard (native), Vial (GUI exports), and QMK c2json. The `LayerAdaptor` handles key reordering between formats — the physical-to-matrix mapping is already solved here.
- **Cluster-based rendering:** Each finger cluster is rendered as a cross/plus pattern with N/S/E/W/Down keys, matching the physical device. This validates the per-cluster visual approach.
- **HLS color space:** Layer colors use HLS for perceptually smooth gradients rather than raw RGB. Worth adopting in our Avalonia implementation.
- **Layer toggle indicators:** Keys that switch layers are color-coded to show which layer they activate, with the target layer's color. Makes navigation between layers intuitive at a glance.
- **Macro display templates:** Uses placeholders like `@1 → @2` to show macro steps compactly on key labels.
- **YAML configuration with defaults:** Sensible defaults with user overrides — a good UX pattern for local settings like layer names, color preferences, etc.

### What Skim Doesn't Do (Our Value-Add)

- No live device connection — requires exported config files
- No interactivity — static image output only
- No system tray / always-available overlay
- No layer detection
- No real-time config reload when the device changes

### Reuse Opportunities

- The format parsing logic (especially Vial → internal model) is directly translatable to C#
- The physical layout coordinate data can be cross-referenced with Keybard-ng's `svalboard-layout.ts`
- The keycode-to-display-label mapping is well-tested and covers edge cases
- The color system (HLS gradients, layer toggle coloring) is worth porting

---

## 15. References

- [Svalboard GitHub Organization](https://github.com/svalboard)
- [Keybard-ng Source](https://github.com/svalboard/keybard-ng) — primary protocol reference
- [Vial Protocol](https://get.vial.today/) — upstream Vial documentation
- [QMK Keycodes](https://docs.qmk.fm/keycodes) — keycode reference
- [QMK Raw HID](https://docs.qmk.fm/features/rawhid) — for Phase 2 layer detection
- [Avalonia UI](https://avaloniaui.net/) — UI framework docs
- [HidSharp](https://www.zer7.com/software/hidsharp) — USB HID library
- [Skim (Svalboard Keymap Image Maker)](https://github.com/Townk/skim) — Python layout renderer, format parsers, color system reference

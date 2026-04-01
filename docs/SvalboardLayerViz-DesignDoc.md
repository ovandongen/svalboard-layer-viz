# SvalboardLayerViz — Design Document

**Author:** Olaf van Dongen
**Date:** April 2026
**Status:** Draft

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
| **Language** | C# / .NET 8+ | Developer's primary skill, cross-platform runtime |
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

Keycodes are 16-bit values with structure depending on the type:

- **Basic keys:** `0x0000–0x00FF` (KC_A = `0x04`, KC_SPACE = `0x2C`, etc.)
- **Modifiers in upper byte:** `(mods << 8) | keycode` where mods is a bitmask (Ctrl=0x01, Shift=0x02, Alt=0x04, GUI=0x08)
- **Layer keys:** Encoded with layer number and type:
  - `MO(layer)` — Momentary (active while held)
  - `TG(layer)` — Toggle on/off
  - `TO(layer)` — Switch permanently
  - `DF(layer)` — Set default layer
  - `TT(layer)` — Tap-toggle
  - `OSL(layer)` — One-shot (next keypress only)
- **Transparent:** `0x0001` (KC_TRNS — falls through to layer below)
- **No key:** `0xFF` / `0x0000` (KC_NO)

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

### Phase 1: Read and Display (MVP)

**Goal:** Connect to Svalboard, read full config, display all layers interactively.

Features:
- System tray icon with click-to-open window
- Auto-detect Svalboard on USB connect/disconnect
- Read full keymap and definition from device
- Visual board layout showing all keys with their labels
- Layer tabs/navigation to switch between layers
- Transparent keys shown with the effective key from the layer below
- Color coding per layer (using Svalboard's layer_colors if available)
- Hotkey to show/hide the overlay

Scope explicitly excluded:
- No config editing
- No auto layer detection
- No Moergo/Glove80 support

### Phase 2: Live Layer Detection

**Goal:** Show which layer is currently active in real-time.

This requires firmware support. Options:
1. **Raw HID query** — add a custom command to Svalboard firmware that reports `layer_state`. QMK supports this via `raw_hid_receive()`/`raw_hid_send()`. Would need a PR to the Svalboard firmware repo.
2. **Macro-based detection** — bind layer toggle keys to also emit a hidden keycode (like F24). The app catches these globally. Simpler but only works for toggle layers, not momentary.
3. **Community collaboration** — propose the Raw HID approach to the Svalboard community. Since it's open source and actively maintained, this seems feasible.

### Phase 3: Enhanced Visualization

- Search/filter keys ("where is Ctrl+C?")
- Key frequency heatmap (if keystroke logging is desired)
- Layout comparison (diff between layers)
- Export to image/PDF for printing
- Multiple board profiles

### Phase 4: Moergo/Glove80 Support

- Add Glove80 physical layout definition
- Adapt to Glove80's ZMK-based config format (different from Vial)
- Unified UI for switching between boards

---

## 8. Project Structure

```
SvalboardLayerViz/
├── SvalboardLayerViz.sln
├── src/
│   ├── SvalboardLayerViz.App/              # Avalonia application
│   │   ├── App.axaml                       # Application entry
│   │   ├── Views/
│   │   │   ├── MainWindow.axaml            # Main overlay window
│   │   │   ├── BoardView.axaml             # Full board visualization
│   │   │   ├── KeyView.axaml               # Single key visual (UserControl)
│   │   │   └── LayerTabsView.axaml         # Layer selector
│   │   ├── ViewModels/
│   │   │   ├── MainWindowViewModel.cs
│   │   │   ├── BoardViewModel.cs
│   │   │   ├── KeyViewModel.cs
│   │   │   └── LayerViewModel.cs
│   │   └── Converters/
│   │       └── KeycodeToDisplayConverter.cs
│   │
│   ├── SvalboardLayerViz.Core/             # Business logic (no UI dependency)
│   │   ├── Protocol/
│   │   │   ├── VialCommands.cs             # Command ID constants
│   │   │   ├── VialProtocolService.cs      # Encode/decode Vial messages
│   │   │   └── XzDecompressor.cs           # XZ decompression for definitions
│   │   ├── Device/
│   │   │   ├── DeviceConnectionService.cs  # HidSharp wrapper, connect/disconnect
│   │   │   └── DeviceInfo.cs               # Device metadata
│   │   ├── Keymap/
│   │   │   ├── KeycodeService.cs           # Translate 16-bit codes to labels
│   │   │   ├── KeymapLoader.cs             # Orchestrate full config load
│   │   │   └── KeycodeDefinitions.cs       # QMK keycode table
│   │   ├── Layout/
│   │   │   ├── SvalboardLayout.cs          # Physical key positions
│   │   │   └── LayoutDefinition.cs         # Parsed definition from device
│   │   └── Models/
│   │       ├── KeyboardConfig.cs           # Full loaded config
│   │       ├── Layer.cs                    # Single layer's keys
│   │       └── Key.cs                      # Key position + keycode
│   │
│   └── SvalboardLayerViz.Tests/            # Unit tests
│       ├── Protocol/
│       │   └── VialProtocolTests.cs
│       ├── Keymap/
│       │   └── KeycodeServiceTests.cs
│       └── TestData/
│           └── sample-keymap.bin           # Captured device data for testing
│
├── docs/
│   └── SvalboardLayerViz-DesignDoc.md      # This document
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

## 12. Open Questions

1. **XZ decompression in .NET** — SharpCompress supports XZ. Need to verify it handles the specific XZ variant Vial uses. Alternative: call out to `xz` CLI.

2. **HidSharp device filtering** — HidSharp can filter by VendorID/ProductID but filtering by UsagePage may require iterating devices. Need to test.

3. **Svalboard-specific extensions** — Keybard-ng references `sval_proto` version and Svalboard-specific features (layer_colors, cosmetic settings). Need to understand what's standard Vial vs. Svalboard-specific.

4. **Multiple Svalboards** — Handle the case where someone has more than one connected? Probably not for Phase 1 but worth considering in the architecture.

5. **Config change detection** — If the user changes their config in Keybard-ng while this app is open, should we detect and reload? Polling vs. manual refresh button.

6. **System tray behavior** — Different platforms handle system tray differently. Avalonia's tray support covers Windows and Linux. macOS uses NSStatusItem. Need to verify Avalonia's cross-platform tray API maturity.

7. **Layer names** — Vial doesn't store layer names. Should the app let users name layers locally? Store in a local config file?

---

## 13. Dependencies

| Package | Purpose | License |
|---------|---------|---------|
| Avalonia | UI framework | MIT |
| Avalonia.Desktop | Desktop platform support | MIT |
| HidSharp | USB HID communication | Apache 2.0 |
| SharpCompress | XZ decompression | MIT |
| CommunityToolkit.Mvvm | MVVM helpers (ObservableObject, RelayCommand) | MIT |
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

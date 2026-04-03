# Svalboard Layer Viz

A cross-platform desktop app that connects to your [Svalboard](https://svalboard.com/) keyboard over USB and visualizes all your layers in real time.

**What it does:**

- Reads your full keymap from the device (no config files needed)
- Shows every key on every layer, matching the Svalboard's unique 5-direction finger cluster layout
- Highlights keys as you press them in real time
- Automatically follows which layer is active when you hold/toggle layer-switch keys
- Works as a transparent overlay you can keep on screen while learning your layout

## Download

Pre-built binaries are available on the [Releases](../../releases/latest) page:

| Platform | Download |
|----------|----------|
| **Windows** (x64) | `SvalboardLayerViz-...-win-x64.zip` |
| **macOS** (Apple Silicon) | `SvalboardLayerViz-...-osx-arm64.zip` |
| **macOS** (Intel) | `SvalboardLayerViz-...-osx-x64.zip` |
| **Linux** (x64) | `SvalboardLayerViz-...-linux-x64.tar.gz` |

**Windows**: Extract the zip and run `SvalboardLayerViz.App.exe`. Windows SmartScreen may warn on first run — click "More info" then "Run anyway".

**macOS**: Extract the zip and open `SvalboardLayerViz.app`. On first launch, right-click the app and select "Open" (or run `xattr -cr SvalboardLayerViz.app` in Terminal) to bypass Gatekeeper.

**Linux**: Extract the tar.gz and run `./install.sh` to install to your home directory with a desktop launcher, or run `./SvalboardLayerViz.App` directly.

---

## Screenshots

*Coming soon*

---

## Building from Source

You need **.NET 10 SDK** installed. Nothing else.

### Install .NET 10 SDK

**macOS** (Homebrew):
```bash
brew install dotnet-sdk
```

**macOS** (manual): Download from https://dotnet.microsoft.com/download/dotnet/10.0

**Windows**: Download the installer from https://dotnet.microsoft.com/download/dotnet/10.0

**Linux** (Ubuntu/Debian):
```bash
sudo apt-get update && sudo apt-get install -y dotnet-sdk-10.0
```

**Linux** (Fedora):
```bash
sudo dnf install dotnet-sdk-10.0
```

Verify it works:
```bash
dotnet --version
# Should print 10.x.x
```

---

## Quick Start

### 1. Clone the repo

```bash
git clone <repo-url>
cd svalboard-layer-viz/svalboard-layer-viz
```

### 2. Build

```bash
dotnet build
```

This downloads all dependencies automatically on first run.

### 3. Run

```bash
dotnet run --project src/SvalboardLayerViz.App
```

The app will open and start looking for your Svalboard. Plug it in via USB if it's not already connected.

### 4. Run tests (optional)

```bash
dotnet test
```

216 tests covering protocol parsing, keycode resolution, layout positioning, and UI logic.

---

## Linux: App Launcher Setup

When you run with `dotnet run`, the app works but isn't integrated into your desktop. To install it as a proper app with an icon and launcher entry:

### Install

```bash
# Publish a self-contained binary
dotnet publish src/SvalboardLayerViz.App -c Release -r linux-x64 --self-contained -o ~/.local/share/svalboard-layer-viz

# Copy the icon
cp src/SvalboardLayerViz.App/Assets/icon.png ~/.local/share/svalboard-layer-viz/icon.png

# Create a .desktop entry
cat > ~/.local/share/applications/svalboard-layer-viz.desktop << 'EOF'
[Desktop Entry]
Name=Svalboard Layer Viz
Exec=/home/YOUR_USERNAME/.local/share/svalboard-layer-viz/SvalboardLayerViz.App
Icon=/home/YOUR_USERNAME/.local/share/svalboard-layer-viz/icon.png
Type=Application
Categories=Utility;
StartupWMClass=SvalboardLayerViz.App
EOF

# Refresh the app menu
update-desktop-database ~/.local/share/applications
```

Replace `YOUR_USERNAME` with your actual username (or use `$HOME` in the paths).

The app will appear in your launcher and can be pinned to the taskbar/dock.

### After code changes

Re-run the `dotnet publish` command above to update the installed binary.

---

## macOS: Dock Icon Setup

When you run with `dotnet run`, macOS shows a generic icon in the dock. For a proper dock icon, use the included `.app` bundle:

### First time setup

Build the self-contained app into the bundle:

```bash
dotnet publish src/SvalboardLayerViz.App -c Release -r osx-arm64 --self-contained -o SvalboardLayerViz.app/Contents/MacOS/bin
```

> **Apple Silicon (M1/M2/M3/M4):** Use `osx-arm64` as shown above.
>
> **Intel Mac:** Use `osx-x64` instead:
> ```bash
> dotnet publish src/SvalboardLayerViz.App -c Release -r osx-x64 --self-contained -o SvalboardLayerViz.app/Contents/MacOS/bin
> ```

### Launch

```bash
open SvalboardLayerViz.app
```

Or double-click `SvalboardLayerViz.app` in Finder.

### After code changes

Re-run the `dotnet publish` command above to update the bundle. The `dotnet run` command always uses the latest code without this step — the bundle is just for the dock icon.

---

## Usage

### Connecting

Plug in your Svalboard via USB. The app detects it automatically and reads the full configuration (layers, keycodes, custom keys). No export files or manual setup needed.

### Layer tabs

Click the layer tabs to switch between layers. Empty layers are hidden. If you've named your layers in the settings, those names appear on the tabs.

### Live key highlighting

Enabled by default. Keys glow white when physically pressed. Toggle with the pulse icon (⚡) in the toolbar.

### Auto layer switching

When enabled, the display automatically follows your active layer:

- **Hold a layer key** (MO, LT) — display switches after the hold threshold
- **Toggle a layer key** (TG) — display flips on each press
- **Release everything** — display returns to base layer

Toggle with the checkmark icon in the toolbar. Reset tracking with the circular arrow if it gets out of sync.

### Settings

Click the gear icon. Settings are split into two tabs:

- **Appearance** — Layer colors, layer names, background transparency
- **Behavior** — Global hotkey (default: F12, macOS/Windows only), layer hold threshold, custom key labels

### Keyboard shortcut

Press **F12** (configurable) to show/hide the overlay from anywhere. On macOS, you may need to grant Accessibility permission in System Settings > Privacy & Security > Accessibility.

> **Linux:** The global hotkey is not available on Linux. Wayland (the default display server on modern Linux distros) blocks applications from receiving keyboard events when their window is not focused, which makes a global hide/show hotkey impossible without special compositor support. Use the taskbar or tray icon to show the window instead.

### Right-click a key

Right-click any key to assign a custom label. Useful for keycodes the app doesn't recognize.

---

## Project Structure

```
svalboard-layer-viz/
├── src/
│   ├── SvalboardLayerViz.App/       # Avalonia UI (views + view models)
│   ├── SvalboardLayerViz.Core/      # Protocol, keymap, layout (no UI)
│   ├── SvalboardLayerViz.Tests/     # Unit tests (216 tests)
│   └── SvalboardLayerViz.Debug/     # Debug utilities (icon generator, etc.)
├── docs/
│   ├── SvalboardLayerViz-DesignDoc.md   # Full design document
│   └── *.md                             # Session changelogs
├── SvalboardLayerViz.app/           # macOS app bundle (dock icon)
└── README.md
```

### How it works

```
USB HID (Svalboard)
  → VialProtocolService        reads keymaps, definitions, matrix state
  → KeymapLoader               assembles full keyboard config
  → KeycodeService              resolves 16-bit QMK codes to labels
  → TransparentKeyResolver      resolves KC_TRNS by walking layers
  → MainWindowViewModel         manages state, polling, auto-layer-switch
  → Avalonia Views              renders the board visualization
```

The app uses the standard Vial/VIA protocol — the same one Vial GUI uses. No firmware modifications needed.

---

## Common Issues

**"No Svalboard found"** — Make sure the keyboard is plugged in via USB. If another app (Vial, Keybard-ng) has the device open, close it first.

**macOS Accessibility permission** — The global hotkey (F12) requires Accessibility access. macOS will prompt you, or go to System Settings > Privacy & Security > Accessibility and add the app.

**macOS "app is damaged" or won't open** — If Gatekeeper blocks the `.app`, run:
```bash
xattr -cr SvalboardLayerViz.app
```

**Auto-layer tracking out of sync** — Click the reset button (circular arrow in toolbar) to return to base layer. Toggle tracking can drift if the app starts while a layer is already toggled.

---

## Tech Stack

| Component | Technology |
|-----------|-----------|
| UI | [Avalonia UI](https://avaloniaui.net/) (.NET, cross-platform XAML) |
| USB | [HidSharp](https://www.zer7.com/software/hidsharp) |
| Protocol | Vial/VIA over USB HID (32-byte reports) |
| Decompression | [SharpCompress](https://github.com/adamhathcock/sharpcompress) (XZ) |
| Hotkeys | [SharpHook](https://github.com/TolikPyl662/SharpHook) (libuiohook) |
| MVVM | [CommunityToolkit.Mvvm](https://learn.microsoft.com/en-us/dotnet/communitytoolkit/mvvm/) |

---

## References

- [Svalboard](https://svalboard.com/) — the keyboard
- [Keybard-ng](https://github.com/svalboard/keybard-ng) — Svalboard web configurator, Vial protocol reference
- [Skim](https://github.com/Townk/skim) — Python layout renderer, color system reference
- [Vial](https://get.vial.today/) — keyboard configuration protocol
- [QMK Keycodes](https://docs.qmk.fm/keycodes) — keycode reference

## License

TBD

# Svalboard Layer Viz

A cross-platform system tray application for visualizing Svalboard keyboard layers.

Connects to your Svalboard via USB, reads the full keymap configuration using the Vial protocol, and displays all layers in an interactive visual layout.

## Status

**Early development** — project scaffold with protocol implementation and UI shell.

## Tech Stack

- **Avalonia UI** — cross-platform XAML framework (Windows, macOS, Linux)
- **HidSharp** — USB HID communication
- **SharpCompress** — XZ decompression for Vial keyboard definitions
- **.NET 8**

## Building

```bash
dotnet restore
dotnet build
dotnet run --project src/SvalboardLayerViz.App
```

## Running Tests

```bash
dotnet test
```

## Project Structure

- `src/SvalboardLayerViz.Core/` — Protocol, device communication, keymap parsing (no UI dependency)
- `src/SvalboardLayerViz.App/` — Avalonia UI application with system tray
- `src/SvalboardLayerViz.Tests/` — Unit tests
- `docs/` — Design documentation

## References

- [Svalboard](https://svalboard.com/)
- [Keybard-ng](https://github.com/svalboard/keybard-ng) — Vial protocol reference
- [Skim](https://github.com/Townk/skim) — Layout rendering reference
- [Avalonia UI](https://avaloniaui.net/)

## License

TBD

#!/bin/bash
# Install Svalboard Layer Viz to ~/.local/share/svalboard-layer-viz
# and create a desktop launcher entry.

set -e

INSTALL_DIR="$HOME/.local/share/svalboard-layer-viz"
DESKTOP_DIR="$HOME/.local/share/applications"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

echo "Installing Svalboard Layer Viz to $INSTALL_DIR ..."

# Copy application files
mkdir -p "$INSTALL_DIR"
cp -r "$SCRIPT_DIR"/* "$INSTALL_DIR/"
chmod +x "$INSTALL_DIR/SvalboardLayerViz.App"

# Install desktop entry
mkdir -p "$DESKTOP_DIR"
cat > "$DESKTOP_DIR/svalboard-layer-viz.desktop" << EOF
[Desktop Entry]
Name=Svalboard Layer Viz
Comment=Keyboard layer visualization for Svalboard
Exec=$INSTALL_DIR/SvalboardLayerViz.App
Icon=$INSTALL_DIR/icon.png
Type=Application
Categories=Utility;HardwareSettings;
StartupWMClass=SvalboardLayerViz.App
Terminal=false
EOF

# Refresh desktop database (if available)
if command -v update-desktop-database &> /dev/null; then
    update-desktop-database "$DESKTOP_DIR" 2>/dev/null || true
fi

echo "Done! Svalboard Layer Viz is now installed."
echo "You can find it in your application launcher, or run:"
echo "  $INSTALL_DIR/SvalboardLayerViz.App"

#!/usr/bin/env bash
set -euo pipefail
source_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
data_home="${XDG_DATA_HOME:-$HOME/.local/share}"
[[ "$data_home" = /* ]] || data_home="$HOME/.local/share"
install_dir="$data_home/soul-revive/app"
mkdir -p -- "$install_dir" "$data_home/applications" "$data_home/icons/hicolor/256x256/apps"
if [[ "$source_dir" != "$(cd -- "$install_dir" && pwd -P)" ]]; then
    cp -R -- "$source_dir/." "$install_dir/"
fi
chmod +x -- "$install_dir/SoulsRevive"
cp -- "$install_dir/soulrevive.png" "$data_home/icons/hicolor/256x256/apps/soulrevive.png"
# Desktop Exec uses its own escaping rules, not shell quoting. Escape a literal
# percent (field-code marker) and the reserved characters inside double quotes.
exec_path="${install_dir//\\/\\\\}/SoulsRevive"
exec_path="${exec_path//\"/\\\"}"
exec_path="${exec_path//\$/\\\$}"
exec_path="${exec_path//\`/\\\`}"
exec_path="${exec_path//%/%%}"
cat > "$data_home/applications/soulrevive.desktop" <<EOF
[Desktop Entry]
Name=Soul Revive
Comment=Pixel bonfire work and break companion
Exec="$exec_path"
Icon=soulrevive
Terminal=false
Type=Application
Categories=Utility;
StartupWMClass=Soul Revive
EOF
if command -v update-desktop-database >/dev/null; then update-desktop-database "$data_home/applications"; fi
printf 'Installed Soul Revive. Open it from your application launcher.\n'

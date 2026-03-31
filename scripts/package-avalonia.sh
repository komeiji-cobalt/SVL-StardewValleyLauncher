#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "$0")/.." && pwd)"
SCRIPT_PATH="$(cd "$(dirname "$0")" && pwd)/$(basename "$0")"
PROJECT="$ROOT_DIR/SVL.Avalonia/SVL.Avalonia.csproj"
APP_NAME="SVL"
APP_BUNDLE_NAME="$APP_NAME.app"
EXECUTABLE_NAME="SVL.Avalonia"
ARTIFACT_PREFIX="SVL.Desktop"
PACKAGE_VERSION="${PACKAGE_VERSION:-1.1.8.6}"
CLI_CONFIG="${1:-}"
RAW_BUILD_CONFIGURATION="${BUILD_CONFIGURATION-}"
RAW_PUBLISH_CONFIG_ENV="${PUBLISH_CONFIG-}"
PUBLISH_CONFIG="${BUILD_CONFIGURATION:-${PUBLISH_CONFIG:-Release}}"
PACKAGE_TARGETS="${PACKAGE_TARGETS:-all}"
HOST_OS="$(uname -s)"

if [[ -z "$CLI_CONFIG" && -z "$RAW_BUILD_CONFIGURATION" && -z "$RAW_PUBLISH_CONFIG_ENV" ]]; then
  echo "[config] 未指定构建配置，默认执行双配置构建: Debug + Release"
  bash "$SCRIPT_PATH" Debug
  bash "$SCRIPT_PATH" Release
  exit 0
fi

if [[ -n "$CLI_CONFIG" ]]; then
  case "$CLI_CONFIG" in
    All|all|Both|both)
      echo "[config] 已启用双配置构建: Debug + Release"
      bash "$SCRIPT_PATH" Debug
      bash "$SCRIPT_PATH" Release
      exit 0
      ;;
    Debug|debug)
      PUBLISH_CONFIG="Debug"
      ;;
    Release|release)
      PUBLISH_CONFIG="Release"
      ;;
    *)
      echo "[ERROR] 无效构建配置: $CLI_CONFIG"
      echo "[USAGE] $0 [Debug|Release]"
      echo "[USAGE] BUILD_CONFIGURATION=Debug $0"
      exit 1
      ;;
  esac
fi

PACKAGE_PROFILE="${PACKAGE_PROFILE:-$PUBLISH_CONFIG}"

if [[ "${ALLOW_PROFILE_CONFIG_MISMATCH:-0}" != "1" && "$PACKAGE_PROFILE" != "$PUBLISH_CONFIG" ]]; then
  echo "[warn] PACKAGE_PROFILE($PACKAGE_PROFILE) 与构建配置($PUBLISH_CONFIG)不一致，已自动使用 $PUBLISH_CONFIG 以避免目录名与产物配置错位"
  PACKAGE_PROFILE="$PUBLISH_CONFIG"
fi

echo "[config] VERSION=$PACKAGE_VERSION PROFILE=$PACKAGE_PROFILE BUILD=$PUBLISH_CONFIG"
echo "[config] TARGETS=$PACKAGE_TARGETS HOST=$HOST_OS"

OUT_BASE="$ROOT_DIR/artifacts/${ARTIFACT_PREFIX}_v${PACKAGE_VERSION}_${PACKAGE_PROFILE}"

if ! command -v dotnet >/dev/null 2>&1; then
  echo "[ERROR] dotnet 未安装。"
  exit 1
fi

case "$PACKAGE_TARGETS" in
  all|All|ALL|windows|Windows|WINDOWS|macos|MacOS|MACOS)
    ;;
  *)
    echo "[ERROR] 无效 TARGETS: $PACKAGE_TARGETS"
    echo "[USAGE] PACKAGE_TARGETS=all|windows|macos $0 [Debug|Release]"
    exit 1
    ;;
esac

echo "[clean] 清理构造目录: $OUT_BASE"
rm -rf "$OUT_BASE"
mkdir -p "$OUT_BASE"

config_marker="$PACKAGE_PROFILE"
if [[ "$PACKAGE_PROFILE" != "$PUBLISH_CONFIG" ]]; then
  config_marker="${PACKAGE_PROFILE}-${PUBLISH_CONFIG}"
fi

publish_rid() {
  local rid="$1"
  local publish_dir="$2"

  echo "[publish] $rid ($PUBLISH_CONFIG)"
  dotnet publish "$PROJECT" -c "$PUBLISH_CONFIG" -r "$rid" --self-contained true -o "$publish_dir"
}

publish_windows_single_file() {
  local rid="$1"
  local publish_dir="$2"

  echo "[publish] $rid ($PUBLISH_CONFIG, single-file)"
  dotnet publish "$PROJECT" -c "$PUBLISH_CONFIG" -r "$rid" --self-contained true \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:EnableCompressionInSingleFile=true \
    -o "$publish_dir"
}

to_windows_path() {
  local path="$1"
  if command -v cygpath >/dev/null 2>&1; then
    cygpath -w "$path"
    return
  fi

  printf '%s' "$path"
}

escape_powershell_single_quotes() {
  printf "%s" "$1" | sed "s/'/''/g"
}

run_powershell_command() {
  local command_text="$1"

  if command -v pwsh >/dev/null 2>&1; then
    pwsh -NoProfile -NonInteractive -Command "$command_text"
    return $?
  fi

  if command -v powershell.exe >/dev/null 2>&1; then
    powershell.exe -NoProfile -NonInteractive -Command "$command_text"
    return $?
  fi

  return 1
}

create_zip_archive() {
  local working_dir="$1"
  local zip_path="$2"
  local source_name="$3"

  rm -f "$zip_path"

  if command -v zip >/dev/null 2>&1; then
    (cd "$working_dir" && zip -qr "$zip_path" "$source_name")
    return
  fi

  if command -v 7z >/dev/null 2>&1; then
    (cd "$working_dir" && 7z a -tzip -mx=9 "$zip_path" "$source_name" >/dev/null)
    return
  fi

  if command -v tar >/dev/null 2>&1; then
    if (cd "$working_dir" && tar -a -cf "$zip_path" "$source_name" >/dev/null 2>&1); then
      return
    fi
  fi

  local ps_working_dir
  local ps_zip_path
  local ps_source_name
  ps_working_dir="$(escape_powershell_single_quotes "$(to_windows_path "$working_dir")")"
  ps_zip_path="$(escape_powershell_single_quotes "$(to_windows_path "$zip_path")")"
  ps_source_name="$(escape_powershell_single_quotes "$source_name")"

  if run_powershell_command "Set-Location -LiteralPath '$ps_working_dir'; Compress-Archive -Path '$ps_source_name' -DestinationPath '$ps_zip_path' -CompressionLevel Optimal -Force"; then
    return
  fi

  echo "[ERROR] 未找到可用的压缩工具（zip/7z/tar/PowerShell Compress-Archive）"
  exit 1
}

build_windows_arch() {
  local rid="$1"
  local arch_out="$OUT_BASE/$rid"
  local publish_dir="$arch_out/publish"
  local arch_name="${rid#win-}"
  local artifact_name="${ARTIFACT_PREFIX}_v${PACKAGE_VERSION}_${config_marker}_Windows_${arch_name}"
  local payload_dir="$OUT_BASE/$artifact_name"
  local zip_path="$OUT_BASE/${artifact_name}.zip"
  local named_exe_path="$OUT_BASE/${artifact_name}.exe"

  publish_windows_single_file "$rid" "$publish_dir"

  rm -rf "$payload_dir"
  mkdir -p "$payload_dir"
  cp -R "$publish_dir/." "$payload_dir/"

  local main_exe="$payload_dir/${EXECUTABLE_NAME}.exe"
  if [[ ! -f "$main_exe" ]]; then
    local fallback_exe
    fallback_exe="$(find "$payload_dir" -maxdepth 1 -type f -name '*.exe' | head -n 1 || true)"
    if [[ -z "$fallback_exe" ]]; then
      echo "[ERROR] 未找到 Windows 可执行文件，RID=$rid"
      exit 1
    fi
    main_exe="$fallback_exe"
  fi

  cp "$main_exe" "$payload_dir/${artifact_name}.exe"
  cp "$main_exe" "$named_exe_path"

  create_zip_archive "$OUT_BASE" "$zip_path" "$artifact_name"

  echo "[OK] $rid 输出:"
  echo "- $payload_dir"
  echo "- $named_exe_path"
  echo "- $zip_path"
}

generate_icns() {
  local png_src="$1"
  local icns_out="$2"

  if [[ ! -f "$png_src" ]]; then
    return 1
  fi

  if ! command -v iconutil >/dev/null 2>&1 || ! command -v sips >/dev/null 2>&1; then
    return 1
  fi

  local iconset_dir
  iconset_dir="$(mktemp -d)/AppIcon.iconset"
  mkdir -p "$iconset_dir"

  sips -z 16 16     "$png_src" --out "$iconset_dir/icon_16x16.png" >/dev/null
  sips -z 32 32     "$png_src" --out "$iconset_dir/icon_16x16@2x.png" >/dev/null
  sips -z 32 32     "$png_src" --out "$iconset_dir/icon_32x32.png" >/dev/null
  sips -z 64 64     "$png_src" --out "$iconset_dir/icon_32x32@2x.png" >/dev/null
  sips -z 128 128   "$png_src" --out "$iconset_dir/icon_128x128.png" >/dev/null
  sips -z 256 256   "$png_src" --out "$iconset_dir/icon_128x128@2x.png" >/dev/null
  sips -z 256 256   "$png_src" --out "$iconset_dir/icon_256x256.png" >/dev/null
  sips -z 512 512   "$png_src" --out "$iconset_dir/icon_256x256@2x.png" >/dev/null
  sips -z 512 512   "$png_src" --out "$iconset_dir/icon_512x512.png" >/dev/null
  sips -z 1024 1024 "$png_src" --out "$iconset_dir/icon_512x512@2x.png" >/dev/null

  iconutil -c icns "$iconset_dir" -o "$icns_out" >/dev/null
  rm -rf "$(dirname "$iconset_dir")"
  return 0
}

build_arch() {
  local rid="$1"
  local arch_out="$OUT_BASE/$rid"
  local app_root="$arch_out/$APP_BUNDLE_NAME"
  local app_contents="$app_root/Contents"
  local app_macos="$app_contents/MacOS"
  local app_resources="$app_contents/Resources"
  local dmg_staging="$arch_out/dmg-staging"
  local arch_name="${rid#osx-}"
  local artifact_name="${ARTIFACT_PREFIX}_v${PACKAGE_VERSION}_${config_marker}_osx_${arch_name}"
  local dmg_path="$OUT_BASE/${artifact_name}.dmg"
  local zip_path="$OUT_BASE/${artifact_name}.zip"
  local icon_src="$ROOT_DIR/SVL.Desktop/Images/icon.png"
  local icon_icns="$app_resources/AppIcon.icns"

  publish_rid "$rid" "$arch_out/publish"

  echo "[bundle] $rid -> $APP_BUNDLE_NAME"
  rm -rf "$app_root"
  mkdir -p "$app_macos" "$app_resources"
  cp -R "$arch_out/publish/." "$app_macos/"

  if generate_icns "$icon_src" "$icon_icns"; then
    echo "[icon] 已生成 $icon_icns"
  elif [[ -f "$icon_src" ]]; then
    cp "$icon_src" "$app_resources/AppIcon.png"
    echo "[icon] 已复制 png 图标（未生成 icns）"
  fi

  cat > "$app_contents/Info.plist" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key>
  <string>$APP_NAME</string>
  <key>CFBundleDisplayName</key>
  <string>$APP_NAME</string>
  <key>CFBundleIdentifier</key>
  <string>io.svl.launcher.$rid</string>
  <key>CFBundleVersion</key>
  <string>$PACKAGE_VERSION</string>
  <key>CFBundleShortVersionString</key>
  <string>$PACKAGE_VERSION</string>
  <key>CFBundlePackageType</key>
  <string>APPL</string>
  <key>CFBundleExecutable</key>
  <string>$EXECUTABLE_NAME</string>
  <key>CFBundleIconFile</key>
  <string>AppIcon</string>
  <key>LSMinimumSystemVersion</key>
  <string>12.0</string>
</dict>
</plist>
EOF

  chmod +x "$app_macos/$EXECUTABLE_NAME" || true

  echo "[dmg] $rid"
  rm -rf "$dmg_staging"
  mkdir -p "$dmg_staging"
  cp -R "$app_root" "$dmg_staging/"
  ln -s /Applications "$dmg_staging/Applications"

  if [[ -f "$icon_icns" ]]; then
    cp "$icon_icns" "$dmg_staging/.VolumeIcon.icns" || true
    if command -v SetFile >/dev/null 2>&1; then
      SetFile -a C "$dmg_staging" || true
      SetFile -a V "$dmg_staging/.VolumeIcon.icns" || true
    fi
  fi

  rm -f "$dmg_path"
  hdiutil create -volname "$APP_NAME" -srcfolder "$dmg_staging" -ov -format UDZO "$dmg_path" >/dev/null

  if [[ -f "$icon_icns" ]]; then
    echo "[icon] 已为 DMG 预置卷图标"
  fi

  echo "[zip] $rid"
  create_zip_archive "$arch_out" "$zip_path" "$APP_BUNDLE_NAME"

  echo "[OK] $rid 输出:"
  echo "- $app_root"
  echo "- $dmg_path"
  echo "- $zip_path"
}

should_build_windows=0
should_build_macos=0

case "$PACKAGE_TARGETS" in
  all|All|ALL)
    should_build_windows=1
    should_build_macos=1
    ;;
  windows|Windows|WINDOWS)
    should_build_windows=1
    ;;
  macos|MacOS|MACOS)
    should_build_macos=1
    ;;
esac

if [[ "$should_build_windows" -eq 1 ]]; then
  build_windows_arch "win-x64"
fi

if [[ "$should_build_macos" -eq 1 ]]; then
  if [[ "$HOST_OS" != "Darwin" ]]; then
    echo "[WARN] 非 macOS 主机，已跳过 macOS DMG 构建"
  elif ! command -v hdiutil >/dev/null 2>&1; then
    echo "[WARN] hdiutil 不可用，已跳过 macOS DMG 构建"
  else
    if ! command -v iconutil >/dev/null 2>&1 || ! command -v sips >/dev/null 2>&1; then
      echo "[WARN] iconutil/sips 不可用，将跳过 icns 生成，App 图标可能不完整。"
    fi
    build_arch "osx-x64"
    build_arch "osx-arm64"
  fi
fi

echo "[DONE] 产物已生成到: $OUT_BASE"
echo "[DONE] Windows 命名示例: ${ARTIFACT_PREFIX}_v${PACKAGE_VERSION}_${PACKAGE_PROFILE}_Windows_x64.exe"
echo "[DONE] macOS 命名示例: ${ARTIFACT_PREFIX}_v${PACKAGE_VERSION}_${PACKAGE_PROFILE}_osx_arm64.dmg"

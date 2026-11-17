#!/bin/bash

set -euo pipefail

project_root="$(cd "$(dirname "$0")/.." && pwd)"
configuration="${CONFIGURATION:-Release}"
architecture="${ARCHITECTURE:-$(uname -m)}"

if [[ "$architecture" != "arm64" && "$architecture" != "x64" ]]; then
    echo "Unsupported architecture: $architecture" >&2
    exit 1
fi

if [[ "$(uname -s)" != "Darwin" ]]; then
    echo "The macOS package must be built on macOS with Xcode command-line tools." >&2
    exit 1
fi

artifacts_dir="$project_root/artifacts"
app_path="$artifacts_dir/RegionLivePreview.app"
contents_path="$app_path/Contents"
macos_path="$contents_path/MacOS"
resources_path="$contents_path/Resources"

mkdir -p "$artifacts_dir"
if [[ -d "$app_path" ]]; then
    rm -rf "$app_path"
fi

mkdir -p "$macos_path" "$resources_path"

dotnet publish "$project_root/RegionLivePreview/RegionLivePreview.csproj" \
    -c "$configuration" \
    -r "osx-$architecture" \
    --self-contained true \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -o "$macos_path"

cp "$project_root/Packaging/macOS/Info.plist" "$contents_path/Info.plist"
cp "$project_root/RegionLivePreview/Assets/app-icon.icns" "$resources_path/app-icon.icns"

identity="${CODESIGN_IDENTITY:--}"
codesign --force --deep --sign "$identity" "$app_path"

echo "Created: $app_path"
echo "Run with: open \"$app_path\""

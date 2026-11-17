#!/bin/bash

set -euo pipefail

project_root="$(cd "$(dirname "$0")/.." && pwd)"
architecture="${ARCHITECTURE:-$(uname -m)}"

if [[ "$architecture" != "arm64" && "$architecture" != "x64" ]]; then
    echo "Unsupported architecture: $architecture" >&2
    exit 1
fi

if [[ "$(uname -s)" != "Darwin" ]]; then
    echo "The macOS package must be built on macOS." >&2
    exit 1
fi

project_file="$project_root/RegionLivePreview/RegionLivePreview.csproj"
version="$(
    sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$project_file" |
    head -n 1
)"

if [[ -z "$version" ]]; then
    echo "Unable to read project version." >&2
    exit 1
fi

ARCHITECTURE="$architecture" \
CODESIGN_IDENTITY="${CODESIGN_IDENTITY:--}" \
"$project_root/scripts/build-macos.sh"

installer_dir="$project_root/artifacts/installers"
mkdir -p "$installer_dir"
output="$installer_dir/RegionLivePreview-$version-$architecture.pkg"

pkg_args=(
    --component "$project_root/artifacts/RegionLivePreview.app"
    --identifier "com.regionlivepreview.app"
    --version "$version"
    --install-location "/Applications"
)

if [[ -n "${INSTALLER_SIGN_IDENTITY:-}" ]]; then
    pkg_args+=(--sign "$INSTALLER_SIGN_IDENTITY")
fi

pkgbuild "${pkg_args[@]}" "$output"

if [[ -n "${APPLE_ID:-}" && -n "${APPLE_TEAM_ID:-}" && -n "${APPLE_APP_PASSWORD:-}" ]]; then
    xcrun notarytool submit "$output" \
        --apple-id "$APPLE_ID" \
        --team-id "$APPLE_TEAM_ID" \
        --password "$APPLE_APP_PASSWORD" \
        --wait
    xcrun stapler staple "$output"
fi

echo "Created: $output"

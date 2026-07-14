#!/usr/bin/env bash
# Portable dev runner for Cassie's fork.
# Extracts the upstream release into a gitignored ./Payload and runs it with the
# repo's patched launch.sh (source of truth), NOT the release's bundled copy.
#
# Usage:
#   ./run-local.sh [path-to-release-tarball]
# If ./Payload already exists, it is reused (no re-extract).
set -euo pipefail

here="$(cd "$(dirname "$(realpath "${BASH_SOURCE[0]}")")" && pwd)"
payload="$here/Payload"
tarball="${1:-$here/MateEngineX_release.tar.gz}"

if [[ ! -d "$payload" ]]; then
  if [[ ! -f "$tarball" ]]; then
    echo "No Payload/ and no tarball at: $tarball" >&2
    echo "Download it first:" >&2
    echo "  gh release download Public-Release-X3.2.0_5 \\" >&2
    echo "    -R Marksonthegamer/Mate-Engine-Linux-Port \\" >&2
    echo "    -p 'MateEngineX_*.tar.gz' -O '$tarball'" >&2
    exit 1
  fi
  echo "Extracting $(basename "$tarball") -> Payload/"
  mkdir -p "$payload"
  # Strip the leading MateEngineX/Payload/ so contents land directly in ./Payload
  tar xzf "$tarball" -C "$payload" --strip-components=2 MateEngineX/Payload
fi

# Always refresh launch.sh from the tracked, patched source of truth.
cp "$here/launch.sh" "$payload/launch.sh"
chmod +x "$payload/launch.sh"

exec "$payload/launch.sh" "$@"

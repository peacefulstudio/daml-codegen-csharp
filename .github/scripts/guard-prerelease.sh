#!/usr/bin/env bash
# Copyright (c) 2026 Peaceful Studio OÜ
# SPDX-License-Identifier: Apache-2.0

set -euo pipefail

usage() {
  cat <<EOF
Usage: $0

Guard: a prerelease codegen-cs must not emit stable packages. When
CODEGEN_CS_VERSION carries a prerelease suffix, fail if any produced nupkg
has a stable (suffix-less) version.

Reads (env):
  CODEGEN_CS_VERSION  codegen-cs version (prerelease => guard active).
  NUPKG_DIR           Directory of produced .nupkg files.
  SOURCE_LABEL        Source label used in the error message.
EOF
  exit "${1:-1}"
}

[[ "${1:-}" == "-h" || "${1:-}" == "--help" ]] && usage 0

shopt -s nullglob
case "$CODEGEN_CS_VERSION" in
  *-*) ;;
  *) echo "codegen-cs $CODEGEN_CS_VERSION is a stable release — prerelease guard not applicable."; exit 0 ;;
esac
violations=0
for pkg in "$NUPKG_DIR"/*.nupkg; do
  ver=$(unzip -p "$pkg" '*.nuspec' | grep -oPm1 '(?<=<version>)[^<]+')
  case "$ver" in
    *-*) ;;
    *) echo "::error::codegen-cs $CODEGEN_CS_VERSION is a prerelease but $(basename "$pkg") is stable version $ver — refusing to publish a final $SOURCE_LABEL package from a non-final codegen."; violations=1 ;;
  esac
done
[ "$violations" -eq 0 ]

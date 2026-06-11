#!/usr/bin/env bash
# Copyright (c) 2026 Peaceful Studio OÜ
# SPDX-License-Identifier: Apache-2.0

set -euo pipefail

usage() {
  cat <<EOF
Usage: $0 [--fix]

Verify every C# and Scala source file carries the SPDX license header.
With --fix, insert the header into any file that is missing it instead of
failing. Backed by google/addlicense.
EOF
}

case "${1:-}" in
  -h | --help)
    usage
    exit 0
    ;;
esac

if ! command -v addlicense >/dev/null 2>&1; then
  echo "addlicense not found. Install: go install github.com/google/addlicense@v1.1.1" >&2
  exit 127
fi

action=(-check)
if [ "${1:-}" = "--fix" ]; then
  action=()
fi

exec addlicense "${action[@]}" -s -l apache -c "Peaceful Studio OÜ" \
  -ignore '**/bin/**' \
  -ignore '**/obj/**' \
  -ignore '**/target/**' \
  -ignore '**/*.proto' \
  -ignore '**/*.xml' \
  -ignore '**/Snapshots/**' \
  -ignore '**/Generated/**' \
  src tests jvm-helper samples

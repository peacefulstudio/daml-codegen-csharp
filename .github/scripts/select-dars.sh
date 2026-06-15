#!/usr/bin/env bash
# Copyright (c) 2026 Peaceful Studio OÜ
# SPDX-License-Identifier: Apache-2.0

set -euo pipefail

usage() {
  cat <<EOF
Usage: $0

Download the source bundle tarball, extract it, copy the DARs matching
DAR_FIND_PATH, then select into ./selected-dars the ones whose basename matches
any of DAR_PATTERNS.

Reads (env):
  DOWNLOAD_URL  Full URL to the source bundle tarball.
  DAR_FIND_PATH find -path glob (relative to ./extracted) locating DARs.
  DAR_PATTERNS  Newline-separated basename globs (without .dar) to select.
  SOURCE_LABEL  Short label used in summary output.
EOF
  exit "${1:-1}"
}

[[ "${1:-}" == "-h" || "${1:-}" == "--help" ]] && usage 0

echo "Downloading $DOWNLOAD_URL"
curl --fail --silent --show-error --location "$DOWNLOAD_URL" -o source-bundle.tar.gz
rm -rf extracted source-dars
mkdir -p extracted source-dars
tar -xzf source-bundle.tar.gz -C extracted
find extracted -path "extracted/$DAR_FIND_PATH" -exec cp -n {} source-dars/ \;

declare -A picked
while IFS= read -r pat; do
  [ -z "$pat" ] && continue
  for f in source-dars/$pat.dar; do
    [ -e "$f" ] && picked["$(basename "$f")"]=1
  done
done <<< "$DAR_PATTERNS"

rm -rf selected-dars && mkdir -p selected-dars
for name in "${!picked[@]}"; do
  cp "source-dars/$name" "selected-dars/$name"
done
count=$(find selected-dars -type f | wc -l | tr -d ' ')
echo "Selected $count DAR(s) for $SOURCE_LABEL:"
find selected-dars -type f
if [ "$count" -eq 0 ]; then
  echo "::error::No DARs matched dar_patterns under $DAR_FIND_PATH"; exit 1
fi

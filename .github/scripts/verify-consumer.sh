#!/usr/bin/env bash
# Copyright (c) 2026 Peaceful Studio OÜ
# SPDX-License-Identifier: Apache-2.0

set -euo pipefail

usage() {
  cat <<EOF
Usage: $0

Prove the generated packages restore and compile for a fresh consumer using
only the local feed plus nuget.org: scaffold a console app, add every package
from the local feed, restore and build.

Reads (env):
  LOCAL_FEED   Directory of generated .nupkg files to consume.
  RUNNER_TEMP  Temp dir for the scaffolded consumer project.
  SOURCE_LABEL Source label used in the success message.
EOF
  exit "${1:-1}"
}

[[ "${1:-}" == "-h" || "${1:-}" == "--help" ]] && usage 0

shopt -s nullglob
CONSUMER="$RUNNER_TEMP/verify-consumer"
rm -rf "$CONSUMER"; mkdir -p "$CONSUMER"
cat > "$CONSUMER/NuGet.config" <<XML
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local-feed" value="$LOCAL_FEED" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
XML
dotnet new console -o "$CONSUMER" --force
pkgs=("$LOCAL_FEED"/*.nupkg)
if [ "${#pkgs[@]}" -eq 0 ]; then
  echo "::error::no packages in local feed to verify"; exit 1
fi
for pkg in "${pkgs[@]}"; do
  ver=$(unzip -p "$pkg" '*.nuspec' | grep -oPm1 '(?<=<version>)[^<]+')
  base=$(basename "$pkg" .nupkg)
  id="${base%."$ver"}"
  echo "add $id $ver"
  dotnet add "$CONSUMER" package "$id" --version "$ver" --no-restore
done
dotnet restore "$CONSUMER"
dotnet build "$CONSUMER" -c Release --no-restore
echo "VERIFY OK [$SOURCE_LABEL]: generated packages restore + compile against [local-feed + nuget.org]."

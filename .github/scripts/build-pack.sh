#!/usr/bin/env bash
# Copyright (c) 2026 Peaceful Studio OÜ
# SPDX-License-Identifier: Apache-2.0

set -euo pipefail

usage() {
  cat <<EOF
Usage: $0

Generate, build and pack the selected DARs into NuGet packages via the
dpm codegen-cs OCI component, retrying across passes so families whose
dependencies pack first can resolve against the local feed. Emits a build
summary and exports the local-feed / nupkg directories to \$GITHUB_ENV.

Reads (env):
  CODEGEN_CS_VERSION  codegen-cs version (a prerelease suffix is forwarded).
  RUNTIME_VERSION     Daml.Runtime version stamped into generated csprojs.
  PACKAGE_LICENSE     SPDX license stamped into generated packages.
  COUNTERS_PATH       Release-counter store passed to codegen-cs.
  RUNNER_TEMP         Temp dir for build output, nupkgs and the local feed.
  GH_ACTOR            Username for the github NuGet source credential.
  GH_TOKEN            Token for the github NuGet source credential.
  SOURCE_LABEL        Short label used in the build summary.
  DRY_RUN             dry-run flag, reported in the build summary.

Writes (when set):
  GITHUB_ENV          LOCAL_FEED, NUPKG_DIR.
  GITHUB_STEP_SUMMARY Per-family build summary table.
EOF
  exit "${1:-1}"
}

[[ "${1:-}" == "-h" || "${1:-}" == "--help" ]] && usage 0

SUFFIX_ARG=""; case "$CODEGEN_CS_VERSION" in *-*) SUFFIX_ARG="--version-suffix ${CODEGEN_CS_VERSION#*-}";; esac
DARS_DIR="$PWD/selected-dars"
OUT_DIR="$RUNNER_TEMP/dar-out"
NUPKG_DIR="$RUNNER_TEMP/dar-nupkgs"
LOCAL_FEED="$RUNNER_TEMP/local-feed"
mkdir -p "$OUT_DIR" "$NUPKG_DIR" "$LOCAL_FEED"
[ -n "${GITHUB_ENV:-}" ] && echo "LOCAL_FEED=$LOCAL_FEED" >> "$GITHUB_ENV"
[ -n "${GITHUB_ENV:-}" ] && echo "NUPKG_DIR=$NUPKG_DIR" >> "$GITHUB_ENV"

export NuGetPackageSourceCredentials_github="Username=${GH_ACTOR};ClearTextPassword=${GH_TOKEN}"

mapfile -t families < <(
  find "$DARS_DIR" -maxdepth 1 -name '*.dar' -exec basename {} .dar \; 2>/dev/null \
    | grep -v -- '-current$' \
    | sed -E 's/-[0-9].*$//' \
    | sort -u
)
echo "Found ${#families[@]} families: ${families[*]}"

declare -A latest_dar
for fam in "${families[@]}"; do
  latest=$(find "$DARS_DIR" -maxdepth 1 -name "${fam}-[0-9]*.dar" -exec basename {} \; \
    | grep -v -- '-current' | sort -V | tail -n 1 || true)
  [ -n "$latest" ] && latest_dar[$fam]="$DARS_DIR/$latest"
done

declare -A status
declare -A reason

build_one() {
  local fam=$1
  local dar=${latest_dar[$fam]}
  local out="$OUT_DIR/$fam"
  rm -rf "$out"; mkdir -p "$out"

  cat > "$out/NuGet.config" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local-feed" value="$LOCAL_FEED" />
    <add key="github" value="https://nuget.pkg.github.com/peacefulstudio/index.json" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
EOF

  # shellcheck disable=SC2086 # $SUFFIX_ARG must word-split into flag + value (or nothing).
  (cd dpm-workspace && dpm codegen-cs \
    --dar "$dar" \
    --out "$out" \
    -- \
    --release-counters "$COUNTERS_PATH" \
    --generate-project \
    --runtime-version "$RUNTIME_VERSION" \
    --package-license "$PACKAGE_LICENSE" \
    --target-framework net10.0 \
    $SUFFIX_ARG \
    --verbosity 1) || { reason[$fam]="codegen"; return 1; }

  local csproj
  csproj=$(find "$out" -maxdepth 2 -name '*.csproj' | head -1)
  if [ -z "$csproj" ]; then
    status[$fam]="skip"
    return 0
  fi
  local pkg_id
  pkg_id=$(basename "$csproj" .csproj)

  (cd "$out" && dotnet build -c Release "$csproj") || { reason[$fam]="build"; return 1; }
  (cd "$out" && dotnet pack -c Release -o "$NUPKG_DIR" "$csproj") || { reason[$fam]="pack"; return 1; }

  cp "$NUPKG_DIR"/"$pkg_id".*.nupkg "$LOCAL_FEED/" || true
  status[$fam]="ok"
  return 0
}

pass=1
while :; do
  echo ""
  echo "########## PASS $pass ##########"
  progress=0
  for fam in "${families[@]}"; do
    [ -n "${status[$fam]:-}" ] && continue
    echo "[$fam] $(basename "${latest_dar[$fam]}")"
    if build_one "$fam"; then
      progress=1
    fi
  done
  [ "$progress" -eq 0 ] && break
  pass=$((pass + 1))
  [ "$pass" -gt 5 ] && { echo "Hit 5-pass cap."; break; }
done

ok_count=0; skip_count=0; fail_count=0
for fam in "${families[@]}"; do
  case "${status[$fam]:-fail}" in
    ok)   ok_count=$((ok_count + 1)) ;;
    skip) skip_count=$((skip_count + 1)) ;;
    *)    fail_count=$((fail_count + 1)) ;;
  esac
done
echo "Result: $ok_count packed, $skip_count skipped, $fail_count failed (of ${#families[@]})"

if [ -n "${GITHUB_STEP_SUMMARY:-}" ]; then
  {
    echo "## $SOURCE_LABEL — Build Summary"
    echo ""
    echo "**Daml.Runtime:** \`$RUNTIME_VERSION\`  "
    echo "**codegen-cs:** \`$CODEGEN_CS_VERSION\`  "
    echo "**dry_run:** \`$DRY_RUN\`"
    echo ""
    echo "| Family | Result | Notes |"
    echo "|--------|--------|-------|"
    for fam in "${families[@]}"; do
      case "${status[$fam]:-fail}" in
        ok)   echo "| \`$fam\` | OK | |" ;;
        skip) echo "| \`$fam\` | SKIP | no exposed types |" ;;
        *)    echo "| \`$fam\` | FAIL | ${reason[$fam]:-unknown} |" ;;
      esac
    done
  } >> "$GITHUB_STEP_SUMMARY"
fi

if [ "$ok_count" -eq 0 ]; then
  echo "::error::No families packed (0 of ${#families[@]}). See per-family logs above."
  exit 1
fi

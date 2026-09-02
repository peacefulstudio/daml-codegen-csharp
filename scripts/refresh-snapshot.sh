#!/usr/bin/env bash
# Copyright 2026 Peaceful Studio OÜ
# SPDX-License-Identifier: Apache-2.0

set -euo pipefail

usage() {
  cat <<EOF
Usage: $0 [--all | <snapshot-name>] [--binpb-only | --skip-binpb] [--helper-jar <path>]

Refreshes a drift-detection snapshot under
tests/Daml.Codegen.CSharp.Tests/Snapshots/<snapshot-name> in two steps:

  1. Regenerates the vendored intermediate.binpb from the DAR vendored beside
     it, using a JVM helper assembled from the working tree's sources — so the
     proto cannot go stale behind a helper change.
  2. Regenerates the expected/ tree from that intermediate.binpb using the
     current codegen source.

Both artifacts are checked in. CI's determinism-gate job (Bundle-level
determinism + conformance drift gate) regenerates every intermediate.binpb the
same way and fails on any byte delta; the drift tests then hold expected/ to the
committed proto, so commit both together.

Options:
  --all                Refresh every snapshot family instead of a single one.
  --binpb-only         Regenerate only the intermediate.binpb protos, skipping
                       the .NET build and the drift tests (CI's drift oracle
                       runs this before comparing the protos against HEAD).
  --skip-binpb         Keep the vendored protos and only regenerate expected/
                       (emitter-only iteration without a JVM toolchain).
  --helper-jar <path>  Path to daml-dar-to-proto.jar. Overriding this skips
                       the freshness check; you own the JAR's currency.
                       (default: the repo JAR, reassembled from source when
                       stale via scripts/ensure-jvm-helper-jar.sh)

Example: $0 splice-api-token-holding-v1
EOF
  exit "${1:-1}"
}

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
SNAPSHOTS_ROOT="$PROJECT_ROOT/tests/Daml.Codegen.CSharp.Tests/Snapshots"

ALL=false
REFRESH_BINPB=true
REFRESH_EXPECTED=true
HELPER_JAR=""
SNAPSHOT_NAME=""

require_value() {
  [[ $# -ge 2 && "$2" != -* ]] || { echo "refresh-snapshot.sh: $1 requires a value" >&2; usage; }
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --all) ALL=true; shift 1 ;;
    --binpb-only) REFRESH_EXPECTED=false; shift 1 ;;
    --skip-binpb) REFRESH_BINPB=false; shift 1 ;;
    --helper-jar) require_value "$@"; HELPER_JAR="$2"; shift 2 ;;
    -h|--help) usage 0 ;;
    -*) echo "refresh-snapshot.sh: unknown arg: $1" >&2; usage ;;
    *)
      if [[ -n "$SNAPSHOT_NAME" ]]; then
        echo "refresh-snapshot.sh: exactly one snapshot name (or --all) expected" >&2
        usage
      fi
      SNAPSHOT_NAME="$1"; shift 1 ;;
  esac
done

if [[ "$REFRESH_BINPB" == false && "$REFRESH_EXPECTED" == false ]]; then
  echo "refresh-snapshot.sh: --binpb-only and --skip-binpb together leave nothing to refresh" >&2
  usage
fi

FAMILIES=()
if [[ "$ALL" == true ]]; then
  if [[ -n "$SNAPSHOT_NAME" ]]; then
    echo "refresh-snapshot.sh: --all takes no snapshot name" >&2
    usage
  fi
  while IFS= read -r family; do
    FAMILIES+=("$family")
  done < <(cd "$SNAPSHOTS_ROOT" && find . -mindepth 1 -maxdepth 1 -type d | sed 's|^\./||' | LC_ALL=C sort)
  if [[ ${#FAMILIES[@]} -eq 0 ]]; then
    echo "refresh-snapshot.sh: no snapshot families found under $SNAPSHOTS_ROOT" >&2
    exit 1
  fi
else
  if [[ -z "$SNAPSHOT_NAME" ]]; then
    usage
  fi
  if [[ "$SNAPSHOT_NAME" =~ [/\\] ]] || [[ "$SNAPSHOT_NAME" == *".."* ]]; then
    echo "Error: snapshot name must not contain path separators or '..': $SNAPSHOT_NAME" >&2
    exit 1
  fi
  if [[ ! -d "$SNAPSHOTS_ROOT/$SNAPSHOT_NAME" ]]; then
    echo "Error: snapshot not found: $SNAPSHOTS_ROOT/$SNAPSHOT_NAME" >&2
    exit 1
  fi
  FAMILIES=("$SNAPSHOT_NAME")
fi

if [[ "$REFRESH_BINPB" == true ]]; then
  for family in "${FAMILIES[@]}"; do
    dar="$SNAPSHOTS_ROOT/$family/$family.dar"
    if [[ ! -f "$dar" ]]; then
      echo "refresh-snapshot.sh: DAR not found: $dar" >&2
      echo "Every snapshot family vendors the DAR its intermediate.binpb is generated from;" >&2
      echo "without it the proto cannot be proven fresh. Vendor the DAR, or pass --skip-binpb" >&2
      echo "to keep the vendored proto." >&2
      exit 1
    fi
  done

  command -v java >/dev/null 2>&1 || { echo "refresh-snapshot.sh: 'java' not found on PATH" >&2; exit 1; }

  if [[ -z "$HELPER_JAR" ]]; then
    HELPER_JAR="$("$SCRIPT_DIR/ensure-jvm-helper-jar.sh")"
  elif [[ ! -f "$HELPER_JAR" ]]; then
    echo "refresh-snapshot.sh: JVM helper JAR not found: $HELPER_JAR" >&2
    echo "Build it with: scripts/ensure-jvm-helper-jar.sh" >&2
    exit 1
  fi

  for family in "${FAMILIES[@]}"; do
    echo "refresh-snapshot.sh: regenerating $family/intermediate.binpb from $family.dar"
    java -jar "$HELPER_JAR" \
      --dar "$SNAPSHOTS_ROOT/$family/$family.dar" \
      --out "$SNAPSHOTS_ROOT/$family/intermediate.binpb" >/dev/null
  done
fi

if [[ "$REFRESH_EXPECTED" == true ]]; then
  dotnet build "$PROJECT_ROOT/src/Daml.Codegen.CSharp.Cli" -c Release

  for family in "${FAMILIES[@]}"; do
    SNAPSHOT_DIR="$SNAPSHOTS_ROOT/$family"
    BINPB_PATH="$SNAPSHOT_DIR/intermediate.binpb"
    EXPECTED_DIR="$SNAPSHOT_DIR/expected"

    if [[ ! -f "$BINPB_PATH" ]]; then
      echo "Error: IntermediateDar proto not found at $BINPB_PATH" >&2
      exit 1
    fi

    STAGING_DIR="$(mktemp -d "$SNAPSHOT_DIR/expected.regen.XXXXXX")"
    trap 'rm -rf "$STAGING_DIR"' EXIT

    dotnet run --project "$PROJECT_ROOT/src/Daml.Codegen.CSharp.Cli" -c Release --no-build -- \
      --intermediate "$BINPB_PATH" \
      -o "$STAGING_DIR" \
      --target-framework net10.0 --verbosity 1

    PLACEHOLDER_PATH="$EXPECTED_DIR/.gitkeep"
    PLACEHOLDER_BACKUP=""
    if [[ -f "$PLACEHOLDER_PATH" ]]; then
      PLACEHOLDER_BACKUP="$(mktemp)"
      cp "$PLACEHOLDER_PATH" "$PLACEHOLDER_BACKUP"
    fi

    rm -rf "$EXPECTED_DIR"
    mv "$STAGING_DIR" "$EXPECTED_DIR"
    trap - EXIT

    if [[ -n "$PLACEHOLDER_BACKUP" ]]; then
      if [[ -z "$(find "$EXPECTED_DIR" -type f)" ]]; then
        mv "$PLACEHOLDER_BACKUP" "$PLACEHOLDER_PATH"
      else
        rm -f "$PLACEHOLDER_BACKUP"
      fi
    fi
  done

  dotnet test --project "$PROJECT_ROOT/tests/Daml.Codegen.CSharp.Tests/Daml.Codegen.CSharp.Tests.csproj" \
    -c Release -- --filter-class "*DriftDetectionTests"
fi

for family in "${FAMILIES[@]}"; do
  if [[ "$REFRESH_BINPB" == true ]]; then
    git -C "$PROJECT_ROOT" add "$SNAPSHOTS_ROOT/$family/intermediate.binpb"
  fi
  if [[ "$REFRESH_EXPECTED" == true ]]; then
    git -C "$PROJECT_ROOT" add "$SNAPSHOTS_ROOT/$family/expected"
  fi
done

echo ""
if [[ "$ALL" == true ]]; then
  echo "Snapshots refreshed and staged: ${FAMILIES[*]}"
else
  echo "Snapshot '${FAMILIES[0]}' refreshed and staged."
fi
echo "Review the diff with: git diff --cached tests/Daml.Codegen.CSharp.Tests/Snapshots"

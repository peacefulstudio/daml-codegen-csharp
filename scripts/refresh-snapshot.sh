#!/usr/bin/env bash
set -euo pipefail

# Refreshes a drift-detection snapshot by regenerating the expected/ tree from
# the vendored intermediate.binpb using the current codegen source.
#
# Usage: ./scripts/refresh-snapshot.sh <snapshot-name>
# Example: ./scripts/refresh-snapshot.sh splice-api-token-holding-v1

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

if [[ $# -lt 1 ]]; then
  echo "Usage: $0 <snapshot-name>"
  echo "Example: $0 splice-api-token-holding-v1"
  exit 1
fi

SNAPSHOT_NAME="$1"
if [[ -z "$SNAPSHOT_NAME" ]]; then
  echo "Error: snapshot name must not be empty"
  exit 1
fi
if [[ "$SNAPSHOT_NAME" =~ [/\\] ]] || [[ "$SNAPSHOT_NAME" == *".."* ]]; then
  echo "Error: snapshot name must not contain path separators or '..': $SNAPSHOT_NAME"
  exit 1
fi
SNAPSHOT_DIR="$PROJECT_ROOT/tests/Daml.Codegen.CSharp.Tests/Snapshots/$SNAPSHOT_NAME"
BINPB_PATH="$SNAPSHOT_DIR/intermediate.binpb"
EXPECTED_DIR="$SNAPSHOT_DIR/expected"

if [[ ! -f "$BINPB_PATH" ]]; then
  echo "Error: IntermediateDar proto not found at $BINPB_PATH"
  exit 1
fi

dotnet build "$PROJECT_ROOT/src/Daml.Codegen.CSharp.Cli" -c Release

STAGING_DIR="$(mktemp -d "$SNAPSHOT_DIR/expected.regen.XXXXXX")"
trap 'rm -rf "$STAGING_DIR"' EXIT

dotnet run --project "$PROJECT_ROOT/src/Daml.Codegen.CSharp.Cli" -c Release --no-build -- \
  --intermediate "$BINPB_PATH" \
  -o "$STAGING_DIR" \
  --target-framework net10.0 --verbosity 1

rm -rf "$EXPECTED_DIR"
mv "$STAGING_DIR" "$EXPECTED_DIR"
trap - EXIT

dotnet test --project "$PROJECT_ROOT/tests/Daml.Codegen.CSharp.Tests/Daml.Codegen.CSharp.Tests.csproj" \
  -c Release -- --filter-class "*DriftDetectionTests"

git -C "$PROJECT_ROOT" add "$EXPECTED_DIR"

echo ""
echo "Snapshot '$SNAPSHOT_NAME' refreshed and staged."
echo "Review the diff with: git diff --cached tests/Daml.Codegen.CSharp.Tests/Snapshots/$SNAPSHOT_NAME/expected"

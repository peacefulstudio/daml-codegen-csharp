#!/usr/bin/env bash
# Copyright 2026 Peaceful Studio OÜ
# SPDX-License-Identifier: Apache-2.0

# End-to-end codegen orchestration: runs the JVM helper to produce an
# IntermediateDar proto from a .dar, then runs the C# CLI to emit .cs.
# Stands in for the dpm codegen-cs OCI bundle entry point during local
# development.

set -euo pipefail

usage() {
  cat <<EOF
Usage: $0 --dar <path-to-dar> --out <output-dir> [--schema-only] [--publish-nuget --nuget-config <path> --nuget-source <name> [--runtime-version <ver>]] [--helper-jar <path>] [--cli-bin <path>]

End-to-end orchestration: runs the JVM helper to produce an IntermediateDar
proto, then runs the C# CLI to emit .cs.

Options:
  --dar                    Path to the input .dar archive (required)
  --out                    Output directory for generated .cs (required)
  --schema-only            Opt the JVM helper into patch-version-insensitive schema-mode
                           decode. Default is full-decode + static party-expression
                           analysis.
  --publish-nuget          Pack and push generated NuGet package after emission.
                           Implies --generate-project. Requires --nuget-config and
                           --nuget-source.
  --nuget-config <path>    Path to NuGet.config (required when --publish-nuget is set).
  --nuget-source <name>    Source name in NuGet.config to push to (required when
                           --publish-nuget is set).
  --runtime-version <ver>  Daml.Runtime NuGet version for the generated .csproj.
                           Recommended when --publish-nuget is set; omitting it
                           leaves a wildcard (*) dependency.
  --helper-jar             Path to daml-codegen-jvm-helper.jar
                           (default: jvm-helper/target/scala-2.13/daml-codegen-jvm-helper.jar)
  --cli-bin                Path to the Daml.Codegen.CSharp.Cli binary
                           (default: src/Daml.Codegen.CSharp.Cli/bin/Release/net10.0/Daml.Codegen.CSharp.Cli)
EOF
  exit "${1:-1}"
}

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

DAR=""
OUT=""
SCHEMA_ONLY=""
PUBLISH_NUGET=""
NUGET_CONFIG=""
NUGET_SOURCE=""
RUNTIME_VERSION=""
HELPER_JAR="$PROJECT_ROOT/jvm-helper/target/scala-2.13/daml-codegen-jvm-helper.jar"
CLI_BIN="$PROJECT_ROOT/src/Daml.Codegen.CSharp.Cli/bin/Release/net10.0/Daml.Codegen.CSharp.Cli"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --dar) DAR="$2"; shift 2 ;;
    --out) OUT="$2"; shift 2 ;;
    --schema-only) SCHEMA_ONLY="--schema-only"; shift 1 ;;
    --publish-nuget) PUBLISH_NUGET="1"; shift 1 ;;
    --nuget-config) NUGET_CONFIG="$2"; shift 2 ;;
    --nuget-source) NUGET_SOURCE="$2"; shift 2 ;;
    --runtime-version) RUNTIME_VERSION="$2"; shift 2 ;;
    --helper-jar) HELPER_JAR="$2"; shift 2 ;;
    --cli-bin) CLI_BIN="$2"; shift 2 ;;
    -h|--help) usage 0 ;;
    *) echo "codegen-pipeline.sh: unknown arg: $1" >&2; usage ;;
  esac
done

[[ -n "$DAR" && -n "$OUT" ]] || usage
[[ -n "${RUNTIME_VERSION}" && -z "${PUBLISH_NUGET}" ]] && { echo "codegen-pipeline.sh: --runtime-version requires --publish-nuget (no .csproj is generated otherwise)" >&2; usage; }
[[ -n "${PUBLISH_NUGET}" && -z "${NUGET_CONFIG}" ]] && { echo "codegen-pipeline.sh: --nuget-config is required when --publish-nuget is set" >&2; usage; }
[[ -n "${PUBLISH_NUGET}" && -z "${NUGET_SOURCE}" ]] && { echo "codegen-pipeline.sh: --nuget-source is required when --publish-nuget is set" >&2; usage; }
if [[ -n "${PUBLISH_NUGET}" ]] && ! command -v dotnet >/dev/null 2>&1; then
  echo "codegen-pipeline.sh: 'dotnet' not found on PATH (.NET SDK required for --publish-nuget)" >&2
  exit 1
fi
[[ -n "${PUBLISH_NUGET}" && -z "${RUNTIME_VERSION}" ]] && echo "codegen-pipeline.sh: warning: --runtime-version not set; generated .csproj will reference Daml.Runtime with wildcard version (*)" >&2
[[ -f "$DAR" ]] || { echo "codegen-pipeline.sh: DAR file not found: $DAR" >&2; exit 1; }
[[ -f "$HELPER_JAR" ]] || {
  echo "codegen-pipeline.sh: JVM helper JAR not found: $HELPER_JAR" >&2
  echo "Build it with: (cd jvm-helper && sbt assembly)" >&2
  exit 1
}
[[ -x "$CLI_BIN" ]] || {
  echo "codegen-pipeline.sh: CLI binary not found or not executable: $CLI_BIN" >&2
  echo "Build it with: dotnet build src/Daml.Codegen.CSharp.Cli -c Release" >&2
  exit 1
}

TOTAL_STEPS=$([[ -n "${PUBLISH_NUGET}" ]] && echo 3 || echo 2)

TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT
INTERMEDIATE="$TMP/intermediate.binpb"

echo "[1/${TOTAL_STEPS}] JVM helper: $DAR -> $INTERMEDIATE${SCHEMA_ONLY:+ (schema-only)}"
java -jar "$HELPER_JAR" --dar "$DAR" --out "$INTERMEDIATE" ${SCHEMA_ONLY}

echo "[2/${TOTAL_STEPS}] C# emitter: $INTERMEDIATE -> $OUT"
CLI_ARGS=(--intermediate "$INTERMEDIATE" -o "$OUT")
[[ -n "${PUBLISH_NUGET}" ]] && CLI_ARGS+=(--generate-project)
[[ -n "${RUNTIME_VERSION}" ]] && CLI_ARGS+=(--runtime-version "${RUNTIME_VERSION}")
"$CLI_BIN" "${CLI_ARGS[@]}"

if [[ -n "${PUBLISH_NUGET}" ]]; then
  echo "[3/${TOTAL_STEPS}] dotnet pack: ${OUT}"
  dotnet pack "${OUT}" -c Release
  mapfile -t nupkgs < <(find "${OUT}/bin/Release" -maxdepth 1 -name '*.nupkg' -not -name '*.symbols.nupkg')
  [[ ${#nupkgs[@]} -gt 0 ]] || { echo "codegen-pipeline.sh: no .nupkg produced" >&2; exit 1; }
  [[ ${#nupkgs[@]} -eq 1 ]] || { echo "codegen-pipeline.sh: multiple .nupkg files found under ${OUT}/bin/Release; refusing to guess which to push" >&2; exit 1; }
  nupkg="${nupkgs[0]}"
  echo "[3/${TOTAL_STEPS}] dotnet nuget push: ${nupkg}"
  dotnet nuget push "${nupkg}" --configfile "${NUGET_CONFIG}" \
    --source "${NUGET_SOURCE}" --skip-duplicate
fi

echo "Done."

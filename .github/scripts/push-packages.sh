#!/usr/bin/env bash
# Copyright (c) 2026 Peaceful Studio OÜ
# SPDX-License-Identifier: Apache-2.0

set -euo pipefail

usage() {
  cat <<EOF
Usage: $0

Push every produced nupkg to GitHub Packages. Refuses to run against an empty
publish, and fails the job if any push fails so the counter persist step does
not run.

Reads (env):
  NUPKG_DIR             Directory of produced .nupkg files.
  GH_TOKEN              Token used as the NuGet push api-key.
  COUNTER_VARIABLE_NAME Repo variable name (named in the failure hint).
EOF
  exit "${1:-1}"
}

[[ "${1:-}" == "-h" || "${1:-}" == "--help" ]] && usage 0

shopt -s nullglob
PKGS=("$NUPKG_DIR"/*.nupkg)
if [ "${#PKGS[@]}" -eq 0 ]; then
  echo "::error::No packages produced — refusing to advance the release counter against an empty publish."
  exit 1
fi
push_failed=0
for pkg in "${PKGS[@]}"; do
  echo "Pushing $pkg"
  if ! dotnet nuget push "$pkg" \
    --source "https://nuget.pkg.github.com/peacefulstudio/index.json" \
    --api-key "$GH_TOKEN" \
    --skip-duplicate; then
    echo "::error::push failed for $pkg"
    push_failed=1
  fi
done
if [ "$push_failed" -ne 0 ]; then
  echo "::error::One or more dotnet nuget push calls failed — failing the job so the release-counter persist step does not run. The locally-mutated counter file is uploaded as the release-counters-snapshot artifact for inspection; restore ${COUNTER_VARIABLE_NAME} from a prior snapshot if needed."
  exit 1
fi

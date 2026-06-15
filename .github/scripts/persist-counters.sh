#!/usr/bin/env bash
# Copyright (c) 2026 Peaceful Studio OÜ
# SPDX-License-Identifier: Apache-2.0

set -euo pipefail

usage() {
  cat <<EOF
Usage: $0

Persist the (possibly mutated) release-counter store back to the repo variable,
creating it if it does not yet exist.

Reads (env):
  COUNTERS_PATH         Path to the local release-counter store.
  COUNTER_VARIABLE_NAME Repo variable name to write.
  GITHUB_REPOSITORY     owner/repo.
  GH_TOKEN              Token with Variables: write on this repo (used by gh).
EOF
  exit "${1:-1}"
}

[[ "${1:-}" == "-h" || "${1:-}" == "--help" ]] && usage 0

if [ ! -f "$COUNTERS_PATH" ]; then
  echo "::error::release-counters file missing at $COUNTERS_PATH"; exit 1
fi
VALUE_JSON=$(jq -Rs '.' < "$COUNTERS_PATH")
PAYLOAD=$(jq -nc --arg name "$COUNTER_VARIABLE_NAME" --argjson value "$VALUE_JSON" \
  '{name: $name, value: $value}')
if gh api "/repos/${GITHUB_REPOSITORY}/actions/variables/${COUNTER_VARIABLE_NAME}" >/dev/null 2>&1; then
  echo "$PAYLOAD" | gh api -X PATCH "/repos/${GITHUB_REPOSITORY}/actions/variables/${COUNTER_VARIABLE_NAME}" --input -
  echo "Updated repo variable ${COUNTER_VARIABLE_NAME} ($(jq 'length' < "$COUNTERS_PATH") entries)."
else
  echo "$PAYLOAD" | gh api -X POST "/repos/${GITHUB_REPOSITORY}/actions/variables" --input -
  echo "Created repo variable ${COUNTER_VARIABLE_NAME} ($(jq 'length' < "$COUNTERS_PATH") entries)."
fi

#!/usr/bin/env bash
# Copyright (c) 2026 Peaceful Studio OÜ
# SPDX-License-Identifier: Apache-2.0

set -euo pipefail

usage() {
  cat <<EOF
Usage: $0

Fetch the release-counter store from the repo variable into a local file and
export its path to \$GITHUB_ENV. A missing variable seeds an empty store.

Reads (env):
  GH_TOKEN              Token with Variables: read on this repo.
  GITHUB_REPOSITORY     owner/repo.
  COUNTER_VARIABLE_NAME Repo variable holding the counter store.
  RUNNER_TEMP           Temp dir for the downloaded store + API body.

Writes (when set):
  GITHUB_ENV  COUNTERS_PATH (path to the fetched store).
EOF
  exit "${1:-1}"
}

[[ "${1:-}" == "-h" || "${1:-}" == "--help" ]] && usage 0

COUNTERS_PATH="$RUNNER_TEMP/release-counters.json"
API_BODY="$RUNNER_TEMP/counter-api-body.json"
HTTP_STATUS=$(curl --silent --show-error --output "$API_BODY" --write-out '%{http_code}' \
  --header "Authorization: Bearer $GH_TOKEN" \
  --header "Accept: application/vnd.github+json" \
  --header "X-GitHub-Api-Version: 2022-11-28" \
  "https://api.github.com/repos/${GITHUB_REPOSITORY}/actions/variables/${COUNTER_VARIABLE_NAME}")
case "$HTTP_STATUS" in
  200)
    jq -r '.value' < "$API_BODY" > "$COUNTERS_PATH"
    echo "Loaded $(jq 'length' < "$COUNTERS_PATH") entries from repo variable ${COUNTER_VARIABLE_NAME}"
    ;;
  404)
    echo "Repo variable ${COUNTER_VARIABLE_NAME} not found — seeding an empty store (first-run bootstrap)."
    echo '{}' > "$COUNTERS_PATH"
    ;;
  *)
    echo "::error::Failed to read ${COUNTER_VARIABLE_NAME} (HTTP $HTTP_STATUS). Check that COUNTERS_TOKEN has 'Variables: read & write' on this repo (per ADR 0004). Body:"
    cat "$API_BODY" >&2
    exit 1
    ;;
esac
[ -n "${GITHUB_ENV:-}" ] && echo "COUNTERS_PATH=$COUNTERS_PATH" >> "$GITHUB_ENV"

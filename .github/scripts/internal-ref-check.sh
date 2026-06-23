#!/usr/bin/env bash
set -euo pipefail

# Fail if any published file cites a GitHub issue/PR by number that is not a
# fully-qualified, allowlisted external reference. Bare numbers (#NNN, "issue
# #NN", "review #NN") and single-segment repo refs (repo#NN) point at the
# private -internal tracker once they reach this public repo, where they
# dangle or mislink. External upstream references are allowed only when fully
# qualified as owner/repo#NN and listed in ALLOWED_EXTERNAL_REFS below.

ROOT="${1:-.}"

# Intentional external upstream references. Add entries consciously — each is a
# third-party tracker we deliberately cite. Pattern is an extended-regex
# matching owner/repo before the '#'.
#
# Note: a purely numeric token such as a six-digit hex colour (#123456) also
# matches '#[0-9]+' and would be reported. No colour-bearing file types are in
# the published set today; if one is ever added, allowlist the specific token
# here or exclude the path in PATHSPECS below.
ALLOWED_EXTERNAL_REFS='(grpc/grpc)#[0-9]+'

BINARY_FIXTURE_EXCLUDES=(':!*.dar' ':!*.binpb' ':!*.png')
OWN_PATTERN_DEFINITION_EXCLUDES=(':!.github/scripts/')
PATHSPECS=('.' "${BINARY_FIXTURE_EXCLUDES[@]}" "${OWN_PATTERN_DEFINITION_EXCLUDES[@]}")

candidates=""
status=0
candidates=$(git -C "$ROOT" grep -nIE -e '#[0-9]+' -- "${PATHSPECS[@]}") || status=$?
if [ "$status" -gt 1 ]; then
  echo "internal-ref-check: git grep failed (status $status)" >&2
  exit 2
fi
if [ -z "$candidates" ]; then
  echo "internal-ref-check: clean"
  exit 0
fi

violations=$(printf '%s\n' "$candidates" | sed -E "s@${ALLOWED_EXTERNAL_REFS}@@g" | grep -E '#[0-9]+' || true)

if [ -n "$violations" ]; then
  {
    echo "internal-ref-check: unqualified or internal issue/PR references found in published files."
    echo "Remove them. If a reference is a genuine external upstream tracker, fully-qualify"
    echo "it as owner/repo#NN and add it to ALLOWED_EXTERNAL_REFS in .github/scripts/internal-ref-check.sh."
    echo
    printf '%s\n' "$violations"
  } >&2
  exit 1
fi

echo "internal-ref-check: clean"

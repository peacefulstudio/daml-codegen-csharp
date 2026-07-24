# Token Standard V2 sample

An offline console app showing the Splice Token Standard V2 (CIP-0112) C#
packages composing with `Daml.Runtime` and `Daml.Ledger.Abstractions`. It
constructs a real V2 workflow — an `IHolding` ACS query, a `TransferInstruction`
accept exercised through its typed interface choice, and an `Allocation`
withdraw — assembled into an unsent command submission, all without contacting
a ledger.

## How it is built

Unlike `samples/QuickstartExample` (which references the `src` projects), this
sample references the **published NuGet surface**: the V2 API packages plus
`Daml.Runtime` / `Daml.Ledger.Abstractions`, resolved from a local feed. It is
deliberately excluded from `Daml.Codegen.CSharp.slnx` and from central package
management (`ManagePackageVersionsCentrally=false`) so the tool's own build
never tries to restore not-yet-published packages, and so package versions can
float on the release-time counter segment.

To build it, populate `local-feed/` with the packed V2 `.nupkg` files and the
packed `Daml.Runtime` / `Daml.Ledger.Abstractions`, then `dotnet build`. CI does
this automatically in the Splice publish pipeline via
`.github/scripts/verify-sample-tokenstandard-v2.sh`, which builds the sample
against the freshly packed feed as a focused V2 compile-gate before publishing.

## Package version floats

The `Splice.*` references float `1.*-*` and the `Daml.*` references float
`0.*-*` — the widest prerelease pattern within each package's current major —
rather than pinning a specific minor/patch. The publish pipeline and
`verify-sample-tokenstandard-v2.sh` pack these into a private `local-feed`, and
`NuGet.config`'s package-source mapping resolves every `Splice.*` / `Daml.*`
package **only** from that feed. So the float always resolves whatever this repo
just packed — a `-preview.N` build today or a stable `M.m.p` build at GA — which
keeps the V2 compile-gate stable across the repo's own version bumps instead of
breaking each time `Directory.Build.props` moves to a new preview minor.

## Why it references the whole V2 family

The sample references the full V2 API family — including
`transfer-events-v2`, `allocation-instruction-v2`, and `allocation-request-v2`,
which its walkthrough does not directly exercise — so the compile-gate proves the
whole family restores and composes together, not only the packages this sample
touches. `splice-token-standard-utils` is deliberately absent: it emits no C#
types and is not published as a package.

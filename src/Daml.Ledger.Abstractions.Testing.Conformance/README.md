# Daml.Ledger.Abstractions.Testing.Conformance

Behavioral conformance kit for `ILedgerClient` implementations: an abstract
xUnit base class, `LedgerClientConformanceTests<TProbe>`, that verifies the
behavioral contract `Daml.Ledger.Abstractions` documents but cannot enforce
by itself.

A transport package subclasses it, supplying a client factory and the
submitter whose visibility scopes the reads:

```csharp
public class MyClientConformanceTests : LedgerClientConformanceTests<MyProbeTemplate>
{
    protected override ILedgerClient CreateClient() => MyClientFactory.CreateSeeded();

    protected override SubmitterInfo Reader { get; } = new Party("alice");
}
```

## Covered contracts

- **Cancellation** — a cancelled live subscription surfaces
  `OperationCanceledException`, not an in-band error.
- **Unclassified surfacing** — a snapshot row the projector cannot classify is
  yielded as `Unclassified`, never silently dropped.
- **Terminal snapshot checkpoint** — the snapshot always ends with a single
  terminal `Checkpoint`, and the seeded active rows precede it.
- **Empty-snapshot checkpoint** — a snapshot with no active contracts (taken at
  `EmptySnapshotOffset`, `LedgerOffset.Begin` by default) still ends with that single
  terminal `Checkpoint`.
- **Offset boundaries `(fromOffset, toOffset]`** — `fromOffset` is exclusive, so
  resuming from a returned offset does not re-deliver the event at it;
  `toOffset` is inclusive and terminal, so a bounded subscription delivers the
  event at `toOffset` and then completes.
- **Non-termination failure mode** — every stream the contract requires to
  terminate is enumerated under a time budget (`StreamTimeout`, default 30s;
  override to widen). A stream that never terminates fails loudly with a
  contract-naming message instead of hanging the run.

## Seeding requirement

`CreateClient()` must return a client seeded with the canonical conformance
scenario:

- at least one active `TProbe` contract and one row the transport cannot fully
  classify (e.g. a missing synchronizer id);
- at least one event on the `SubscribeAsync` stream at a known offset, with the
  `(fromOffset, toOffset]` bounds honored;
- `GetLedgerEndAsync` returning the seeded ledger end;
- an empty active-contract-set snapshot at `EmptySnapshotOffset` (defaults to
  `LedgerOffset.Begin`; override it if your transport rejects an active-contract-set
  query at offset 0 with `INVALID_ARGUMENT`, pointing it at a known-empty offset);
- a live subscription that honors cancellation.

The inherited `[Fact]` methods then exercise that seeded client against the
documented contract.

Not for production use.

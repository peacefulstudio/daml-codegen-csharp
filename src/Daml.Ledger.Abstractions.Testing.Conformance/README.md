# Daml.Ledger.Abstractions.Testing.Conformance

Behavioral conformance kit for `ILedgerClient` implementations: an abstract
xUnit base class, `LedgerClientConformanceTests<TProbe>`, that verifies the
behavioral contract `Daml.Ledger.Abstractions` documents but cannot enforce
by itself.

This kit is for *implementers* of `ILedgerClient`. To unit-test
*application code* that consumes an `ILedgerClient`, don't hand-roll a fake
— use `Canton.Ledger.Testing` (published from
[`canton-ledger-api-csharp`](https://github.com/peacefulstudio/canton-ledger-api-csharp)):
its `FakeLedgerClient` is a stageable in-memory implementation, with
builders for the fiddly event/result types and no mocking framework
required.

A transport package subclasses it, supplying a client factory and the
submitter whose visibility scopes the reads. `TProbe` is a Daml template in the
template family (`ITemplate, IDamlRecord<TProbe>`) — a generated template already
carries both facets:

```csharp
public class MyClientConformanceTests : LedgerClientConformanceTests<MyProbeTemplate>
{
    protected override ILedgerClient CreateClient() => MyClientFactory.CreateSeeded();

    protected override SubmitterInfo Reader { get; } = new Party("alice");
}
```

## Scope: the template family only

Every check below drives the template-family read surface — `SubscribeAsync<T>`,
`SubscribeActiveAsync<T>`, `SubscribeLedgerEffectsAsync<T>`. The interface-family
overloads that take a marker's `View` witness are **not exercised**: the kit would need
an interface marker and view record from the adopter's own corpus to drive them, which is
a fixture this kit does not yet ask for. An implementation whose
`SubscribeAsync<TInterface, TView>` gets its offset bounds, cancellation or view-absent
downgrade wrong will still pass this suite. Until that fixture exists, hold those three
members to the contract `ILedgerStreamer` documents by your own tests.

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
- **Fault surfacing (opt-in)** — a mid-snapshot transport fault surfaces in-band as a
  terminal `AcsSnapshotEntry<T>.StreamError` in place of the `Checkpoint`, never thrown,
  so a caller draining the snapshot handles faults as values. Skipped unless the adopter
  overrides `CreateFaultingSnapshotClient()` to return a client whose snapshot faults
  mid-stream; the default returns `null` because inducing a deterministic mid-snapshot
  fault is transport-specific.
- **Offset boundaries `(fromOffset, toOffset]`** — `fromOffset` is exclusive, so
  resuming from a returned offset does not re-deliver the event at it;
  `toOffset` is inclusive and terminal, so a bounded subscription delivers the
  event at `toOffset` and then completes.
- **Non-termination failure mode** — every stream the contract requires to
  terminate is enumerated under a time budget (`StreamTimeout`, default 30s;
  override to widen). A stream that never terminates fails loudly with a
  contract-naming message instead of hanging the run.
- **Submitter authority (opt-in)** — `SubmitAndWaitAsync` and
  `TrySubmitAndWaitForTransactionAsync` apply the `submitter` parameter
  authoritatively via `CommandsSubmission.WithSubmitter`, overwriting any
  `ActAs` already set on the submission, rather than dispatching whatever the
  caller pre-set. Skipped unless the adopter overrides `CreateWriteFixture()`
  to return a client that accepts a submission from one party and rejects it
  from another.
- **Command-id deduplication (opt-in)** — `TryExerciseAsync` and `TryCreateAsync`
  dispatch a caller-supplied `commandId` to the participant verbatim, and mint a
  fresh one only when the caller omits it, never leaving the participant's
  `command_id` unset. Both directions are checked: a client that mints over the
  supplied id breaks the first, a client that never mints breaks the second, and
  each is a distinct fault. Skipped unless the adopter overrides
  `CreateCommandIdFixture()`.

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

To also cover the fault path, override `CreateFaultingSnapshotClient()` to return a
separate client whose snapshot faults mid-stream (yielding a terminal
`AcsSnapshotEntry<T>.StreamError` and no `Checkpoint`). Leaving it at its `null` default
skips only the fault-surfacing check.

To also cover submitter authority, override `CreateWriteFixture()` to return a
`WriteConformanceFixture`: a fresh client plus a submission it accepts from an
`Authorized` party and rejects from an `Unauthorized` one. Leaving it at its `null`
default skips only the submitter-authority checks.

To also cover command-id deduplication, override `CreateCommandIdFixture()` to return a
`CommandIdConformanceFixture`: a fresh client, one exercise and one create it accepts
(each forwarding the fixture's `CommandId?` argument to the call's `commandId` parameter
verbatim, `null` included), and a read-back of the `command_id` the participant recorded
for the submission just dispatched — as a raw string, so an unset id reads back as `null`
rather than being smuggled past the check by a `default(CommandId)`. Leaving it at its
`null` default skips only the command-id checks.

Not for production use.

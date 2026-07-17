# Daml.Ledger.Abstractions

Transport-agnostic abstractions for Daml ledger clients.

`ILedgerClient` is the composition of three capability interfaces, each
covering one slice of ledger interaction:

- `ILedgerWriter` — submit commands, create contracts, exercise choices.
- `ILedgerReader` — query the participant's ledger end.
- `ILedgerStreamer` — subscribe to contract events and active-contract-set
  snapshots.

A transport implements the split interfaces (and therefore `ILedgerClient`);
a consumer that only needs to read or stream can depend on `ILedgerReader` or
`ILedgerStreamer` alone. Implementations live in their own transport
packages:

- (planned, not yet published) gRPC client for Canton participants
- (planned) HTTP REST client for the Daml JSON Ledger API
- in-memory test fakes for application testing

Derived convenience methods — single-`Party` overloads, the throwing
`ExerciseAsync` wrappers, and the create-by-exercise helpers
(`TryCreateOneByExerciseAsync`, `TryCreateManyByExerciseAsync`, and their
throwing `CreateOneByExerciseAsync`/`CreateManyByExerciseAsync` twins) — live in
`Daml.Ledger.Abstractions.Extensions` and are opt-in via `using
Daml.Ledger.Abstractions.Extensions;`. The capability interfaces stay free of
this sugar, so a consumer that only needs the `SubmitterInfo`-based
primitives never has it in scope.

Ledger positions are the `LedgerOffset` value type, never a raw `long`.

Generated codegen output (`<Choice>Async` extensions, projector helpers)
references this package — never a transport-specific one — so consumers
that only need projectors do not transitively depend on a gRPC stack.

Versioned in lockstep with `Daml.Runtime` and `Daml.Codegen.CSharp`.

Interface-only package. `Daml.Ledger.Abstractions.Testing.Conformance` ships the
shared behavioral conformance kit — a transport implementation subclasses it
to verify it upholds this package's documented contract (cancellation,
unclassified-row surfacing, terminal checkpoint, seeded-row ordering) —
beyond that, implementers carry their own contract tests.

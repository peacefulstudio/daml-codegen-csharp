# Daml.Ledger.Abstractions

Transport-agnostic abstractions for Daml ledger clients.

Defines `ILedgerClient` — the single contract any Daml ledger transport
implements. Implementations live in their respective transport packages:

- (planned, not yet published) gRPC client for Canton participants
- (planned) HTTP REST client for the Daml JSON Ledger API
- in-memory test fakes for application testing

Generated codegen output (`<Choice>Async` extensions, projector helpers)
references this package — never a transport-specific one — so consumers
that only need projectors do not transitively depend on a gRPC stack.

Versioned in lockstep with `Daml.Runtime` and `Daml.Codegen.CSharp`.

Interface-only package — implementers carry their own contract tests.

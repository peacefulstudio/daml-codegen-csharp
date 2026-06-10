# Conformance corpus

DAML models that define the type shapes codegen claims to support. Compiled to
`richtypes.dar` and shipped (compiled + embedded) in `Daml.Codegen.Testing.Conformance`.

The committed `.dar` is the source of truth for the package build. To grow the
corpus (new type shapes), edit `RichTypes.daml`, then rebuild and commit the DAR:

    cd conformance/richtypes && dpm build && cp .daml/dist/richtypes-*.dar ./richtypes.dar

The conformance-package and live-ledger-gate rationale is recorded in ADR 0006, kept in the project's internal ADR collection.

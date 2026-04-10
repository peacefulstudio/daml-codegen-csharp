using Daml.Runtime.Data;

namespace Daml.Runtime.Contracts;

/// <summary>
/// Represents a contract creation event from the ledger.
/// </summary>
public sealed record CreatedEvent(
    string EventId,
    string ContractId,
    Identifier TemplateId,
    DamlRecord CreateArguments,
    IReadOnlyList<Party> WitnessParties,
    IReadOnlyList<Party> Signatories,
    IReadOnlyList<Party> Observers,
    ContractKey? ContractKey = null,
    DateTimeOffset? CreatedAt = null);

/// <summary>
/// Represents a contract key.
/// </summary>
public sealed record ContractKey(DamlValue Value, Identifier? TemplateId = null);

/// <summary>
/// Represents an archived (consumed) contract event.
/// </summary>
public sealed record ArchivedEvent(
    string EventId,
    string ContractId,
    Identifier TemplateId,
    IReadOnlyList<Party> WitnessParties);

// Copyright (c) 2026 Peaceful Studio OÜ. All rights reserved.

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

/// <summary>
/// Represents a choice-exercise event observed in a transaction. Carries the
/// wire-level <see cref="ExerciseResult"/> so codegen-emitted choice wrappers
/// can deserialize the choice's typed return value (e.g. project a
/// <c>choice GetTrailingTwap : Decimal</c> result to <c>ExerciseOutcome&lt;decimal&gt;</c>).
/// </summary>
/// <param name="ContractId">The on-ledger contract ID the choice was exercised on.</param>
/// <param name="TemplateId">The template that defines the executed choice. The package
/// id may differ from the target contract's package id when the contract has been
/// upgraded or downgraded.</param>
/// <param name="InterfaceId">When the choice is inherited from an interface, the
/// interface identifier; <c>null</c> for choices defined directly on the template.</param>
/// <param name="ChoiceName">The choice that was exercised on the target contract.</param>
/// <param name="ChoiceArgument">The argument value passed to the choice. Wire-level
/// <see cref="DamlValue"/>; codegen-emitted wrappers deserialize to the typed argument.</param>
/// <param name="ExerciseResult">The result returned by the choice. Wire-level
/// <see cref="DamlValue"/>; codegen-emitted wrappers deserialize to the typed return.</param>
/// <param name="Consuming">Whether the exercise consumed (archived) the target contract.</param>
/// <param name="ActingParties">Parties that exercised the choice.</param>
/// <param name="WitnessParties">Parties notified of this event.</param>
public sealed record ExercisedEvent(
    string ContractId,
    Identifier TemplateId,
    Identifier? InterfaceId,
    string ChoiceName,
    DamlValue ChoiceArgument,
    DamlValue ExerciseResult,
    bool Consuming,
    IReadOnlyList<Party> ActingParties,
    IReadOnlyList<Party> WitnessParties);

// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json.Serialization;

namespace Daml.Runtime;

/// <summary>
/// A resume ticket for the stakeholder-based live stream, handed back by an active-contract-set
/// snapshot's terminal checkpoint. Only the stakeholder-based subscription accepts it — there is
/// deliberately no implicit conversion to <see cref="LedgerOffset"/>, so passing it to a
/// witness-based subscription does not compile. The raw offset stays reachable via
/// <see cref="Offset"/> for a deliberate cross-basis resume.
/// See <see href="https://docs.canton.network/reference/json-api-reference/post-v2updates">Canton
/// Ledger API — ACS-delta (stakeholder) vs ledger-effects (witness) update filtering</see>
/// for the visibility basis this ticket protects.
/// </summary>
/// <param name="Offset">The underlying ledger offset.</param>
/// <remarks>
/// <see cref="System.Text.Json"/> reaches a struct through its implicit parameterless
/// constructor unless told otherwise, which leaves <paramref name="Offset"/> an init-only
/// property carrying no constructor parameter — and a resume ticket whose offset no payload
/// mentions then reads as <see cref="LedgerOffset.Begin"/>. Naming the primary constructor puts
/// the parameter back within reach of
/// <see cref="Serialization.DamlJsonConverters.AddDamlConverters"/>, which requires it.
/// </remarks>
[method: JsonConstructor]
public readonly record struct StakeholderResume(LedgerOffset Offset);

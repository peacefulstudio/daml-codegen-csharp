// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using AwesomeAssertions;
using Daml.Codegen.Testing.Conformance.Contractkeys;
using Daml.Codegen.Testing.Conformance.Richtypes;
using Xunit;

namespace Daml.Codegen.Testing.Conformance.Tests;

/// <summary>
/// System.Text.Json reads <c>[JsonConverter]</c> off the type being converted and does not
/// walk the base chain, so the attribute on <c>ContractId&lt;T&gt;</c> never reached the
/// emitted <c>T.ContractId</c> — the type a consumer DTO actually declares. These run against
/// the corpus's own generated types rather than a hand-written stand-in, so they cannot drift
/// away from what the emitter writes.
/// </summary>
public class EmittedContractIdJsonTests
{
    private sealed record AccountReference(Account.ContractId Id);

    [Fact]
    public void Emitted_ContractId_serializes_as_a_bare_string_without_AddDamlConverters()
    {
        JsonSerializer.Serialize(new Account.ContractId("00abc")).Should().Be(
            "\"00abc\"",
            "the emitter writes the converter attribute onto the generated record, so the wire shape no longer depends on the consumer remembering to register converters");
    }

    [Fact]
    public void Emitted_ContractId_deserializes_from_a_bare_string_without_AddDamlConverters()
    {
        var contractId = JsonSerializer.Deserialize<Account.ContractId>("\"00abc\"");

        contractId.Should().BeOfType<Account.ContractId>("reading must construct the derived record, not its base");
        contractId!.Value.Should().Be("00abc");
    }

    [Fact]
    public void Emitted_ContractId_round_trips_inside_a_dto_without_AddDamlConverters()
    {
        const string json = "{\"Id\":\"00abc\"}";

        var reference = JsonSerializer.Deserialize<AccountReference>(json);

        reference!.Id.Value.Should().Be("00abc");
        JsonSerializer.Serialize(reference).Should().Be(json,
            "a DTO field declared as the emitted nested type is the shape the gap was about");
    }

    [Fact]
    public void Emitted_ContractId_carries_the_converter_on_a_keyless_template_too()
    {
        JsonSerializer.Serialize(new Asset.ContractId("00def")).Should().Be(
            "\"00def\"",
            "the attribute is emitted from the contract-id writer, which does not consult the key");
    }
}

// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using System.Text.Json.Serialization;
using AwesomeAssertions;
using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Serialization;
using Xunit;

namespace Daml.Runtime.Tests;

/// <summary>
/// Pins the wire shape of <see cref="LedgerOffset"/> — the bare JSON number a participant
/// puts on the <c>offset</c> field — and the read that recovers it.
/// </summary>
public class LedgerOffsetJsonTests
{
    private static readonly JsonSerializerOptions Options = new JsonSerializerOptions().AddDamlConverters();

    [Fact]
    public void LedgerOffset_serializes_as_a_bare_JSON_number()
    {
        JsonSerializer.Serialize(LedgerOffset.At(7)).Should().Be("7");
    }

    [Fact]
    public void LedgerOffset_reads_back_the_offset_it_wrote()
    {
        var restored = JsonSerializer.Deserialize<LedgerOffset>(JsonSerializer.Serialize(LedgerOffset.At(6057)));

        restored.Should().Be(LedgerOffset.At(6057));
    }

    [Fact]
    public void StakeholderResume_reads_back_the_offset_it_wrote()
    {
        var resume = new StakeholderResume(LedgerOffset.At(6057));

        var restored = JsonSerializer.Deserialize<StakeholderResume>(JsonSerializer.Serialize(resume));

        restored.Should().Be(resume);
    }

    [Fact]
    public void LedgerOffset_rejects_a_non_numeric_token()
    {
        var act = () => JsonSerializer.Deserialize<LedgerOffset>("\"6057\"");

        act.Should().Throw<JsonException>().WithMessage("*LedgerOffset*String*");
    }

    [Fact]
    public void LedgerOffset_reads_back_the_largest_offset_a_participant_can_reach()
    {
        var restored = JsonSerializer.Deserialize<LedgerOffset>(
            JsonSerializer.Serialize(LedgerOffset.At(long.MaxValue)));

        restored.Should().Be(LedgerOffset.At(long.MaxValue));
    }

    [Fact]
    public void LedgerOffset_rejects_a_negative_offset()
    {
        var act = () => JsonSerializer.Deserialize<LedgerOffset>("-1");

        act.Should().Throw<JsonException>()
            .WithMessage("*LedgerOffset*negative*-1*")
            .WithInnerException<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void LedgerOffset_rejects_a_fractional_offset()
    {
        var act = () => JsonSerializer.Deserialize<LedgerOffset>("6057.5");

        act.Should().Throw<JsonException>().WithMessage("*LedgerOffset*whole-number literal*6057.5*");
    }

    [Fact]
    public void LedgerOffset_rejects_an_offset_beyond_Int64()
    {
        var act = () => JsonSerializer.Deserialize<LedgerOffset>("9223372036854775808");

        act.Should().Throw<JsonException>()
            .WithMessage("*LedgerOffset*Int64 range*9223372036854775808*");
    }

    [Fact]
    public void LedgerOffset_rejects_an_exponent_form_that_names_a_whole_number()
    {
        var act = () => JsonSerializer.Deserialize<LedgerOffset>("1e2");

        act.Should().Throw<JsonException>().WithMessage("*LedgerOffset*exponent*1e2*");
    }

    [Fact]
    public void LedgerOffset_rejects_a_null_on_a_non_nullable_member()
    {
        var act = () => JsonSerializer.Deserialize<StakeholderResume>("{\"Offset\":null}");

        act.Should().Throw<JsonException>().WithMessage("*LedgerOffset*Null*");
    }

    [Fact]
    public void A_nullable_offset_reads_a_null_as_absent_rather_than_refusing_it()
    {
        var restored = JsonSerializer.Deserialize<Checkpoint>("{\"Offset\":null}");

        restored!.Offset.Should().BeNull();
    }

    [Fact]
    public void A_nullable_offset_reads_back_the_offset_it_wrote()
    {
        var checkpoint = new Checkpoint(LedgerOffset.At(6057));

        var restored = JsonSerializer.Deserialize<Checkpoint>(JsonSerializer.Serialize(checkpoint));

        restored.Should().Be(checkpoint);
    }

    [Fact]
    public void StakeholderResume_throws_JsonException_when_the_offset_is_absent()
    {
        var act = () => JsonSerializer.Deserialize<StakeholderResume>("{}", Options);

        act.Should().Throw<JsonException>().WithMessage("*Offset*");
    }

    [Fact]
    public void TransactionResult_throws_JsonException_when_CompletionOffset_is_absent()
    {
        var json = """
            {"UpdateId":"u1","CreatedContracts":[],"ArchivedContractIds":[]}
            """;

        var act = () => JsonSerializer.Deserialize<TransactionResult>(json, Options);

        act.Should().Throw<JsonException>().WithMessage("*CompletionOffset*");
    }

    [Fact]
    public void AddDamlConverters_leaves_a_nullable_offset_parameter_optional()
    {
        var restored = JsonSerializer.Deserialize<Checkpoint>("{}", Options);

        restored!.Offset.Should().BeNull();
    }

    [Fact]
    public void AddDamlConverters_leaves_an_offset_parameter_that_carries_a_default_optional()
    {
        var restored = JsonSerializer.Deserialize<ResumeFromBegin>("""{"Note":"n"}""", Options);

        restored!.Offset.Should().Be(LedgerOffset.Begin);
    }

    [Fact]
    public void AddDamlConverters_writes_a_required_offset_that_DefaultIgnoreCondition_would_drop()
    {
        var options = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
        }.AddDamlConverters();
        var resume = new StakeholderResume(LedgerOffset.Begin);

        var restored = JsonSerializer.Deserialize<StakeholderResume>(
            JsonSerializer.Serialize(resume, options),
            options);

        restored.Should().Be(resume);
    }

    [Fact]
    public void StakeholderResume_bare_options_reads_omitted_offset_as_Begin()
    {
        var restored = JsonSerializer.Deserialize<StakeholderResume>("{}");

        restored.Offset.Should().Be(LedgerOffset.Begin);
    }

    [Fact]
    public void StakeholderResume_reads_back_an_offset_of_Begin_the_payload_states()
    {
        var restored = JsonSerializer.Deserialize<StakeholderResume>("""{"Offset":0}""", Options);

        restored.Offset.Should().Be(LedgerOffset.Begin);
    }

    private sealed record Checkpoint(LedgerOffset? Offset);

    private sealed record ResumeFromBegin(string Note, LedgerOffset Offset = default);
}

// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using AwesomeAssertions;
using Daml.Runtime.Commands;
using Daml.Runtime.Data;
using Xunit;

namespace Daml.Runtime.Tests;

/// <summary>
/// Pins the wire shape of <see cref="SubmitterInfo"/> — the two party arrays that carry
/// <c>Commands.act_as</c> and <c>Commands.read_as</c> — and the read that recovers them rather
/// than yielding a value whose <see cref="SubmitterInfo.ActAs"/> throws at the use site.
/// </summary>
public class SubmitterInfoJsonTests
{
    private static readonly JsonSerializerOptions CamelCase =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private static readonly JsonSerializerOptions CaseInsensitive =
        new() { PropertyNameCaseInsensitive = true };

    private sealed record Submission(SubmitterInfo Submitter);

    private sealed record OptionalSubmission(SubmitterInfo? Submitter);

    [Fact]
    public void SubmitterInfo_writes_both_party_sets_as_JSON_arrays()
    {
        var submitter = new SubmitterInfo(new Party("alice"), new HashSet<Party> { new("bob") });

        JsonSerializer.Serialize(submitter).Should().Be("{\"ActAs\":[\"alice\"],\"ReadAs\":[\"bob\"]}");
    }

    [Fact]
    public void SubmitterInfo_writes_an_absent_ReadAs_as_the_empty_array()
    {
        JsonSerializer.Serialize(new SubmitterInfo(new Party("alice"))).Should()
            .Be("{\"ActAs\":[\"alice\"],\"ReadAs\":[]}");
    }

    [Fact]
    public void SubmitterInfo_reads_back_the_parties_it_wrote()
    {
        var submitter = new SubmitterInfo(
            new HashSet<Party> { new("alice"), new("carol") },
            new HashSet<Party> { new("bob") });

        var restored = JsonSerializer.Deserialize<SubmitterInfo>(JsonSerializer.Serialize(submitter));

        restored.Should().Be(submitter);
    }

    [Fact]
    public void Submission_reads_back_the_submitter_it_wrote()
    {
        var submission = new Submission(new Party("alice"));

        var restored = JsonSerializer.Deserialize<Submission>(JsonSerializer.Serialize(submission));

        restored.Should().Be(submission);
    }

    [Fact]
    public void SubmitterInfo_reads_an_empty_ReadAs_back_as_the_empty_set()
    {
        var restored = JsonSerializer.Deserialize<SubmitterInfo>("{\"ActAs\":[\"alice\"],\"ReadAs\":[]}");

        restored.ReadAs.Should().BeEmpty();
        restored.ActAs.Should().BeEquivalentTo([new Party("alice")]);
    }

    [Fact]
    public void SubmitterInfo_reads_an_omitted_ReadAs_back_as_the_empty_set()
    {
        var restored = JsonSerializer.Deserialize<SubmitterInfo>("{\"ActAs\":[\"alice\"]}");

        restored.ReadAs.Should().BeEmpty();
    }

    [Fact]
    public void SubmitterInfo_ignores_a_property_it_does_not_know()
    {
        var restored = JsonSerializer.Deserialize<SubmitterInfo>(
            "{\"ActAs\":[\"alice\"],\"Unknown\":{\"nested\":[1,2]}}");

        restored.Should().Be(new SubmitterInfo(new Party("alice")));
    }

    [Fact]
    public void SubmitterInfo_rejects_a_payload_that_omits_ActAs()
    {
        var act = () => JsonSerializer.Deserialize<SubmitterInfo>("{\"ReadAs\":[\"bob\"]}");

        act.Should().Throw<JsonException>().WithMessage("*SubmitterInfo*ActAs*");
    }

    [Fact]
    public void SubmitterInfo_rejects_an_empty_ActAs()
    {
        var act = () => JsonSerializer.Deserialize<SubmitterInfo>("{\"ActAs\":[],\"ReadAs\":[]}");

        act.Should().Throw<JsonException>().WithMessage("*SubmitterInfo*at least one party*");
    }

    [Fact]
    public void SubmitterInfo_rejects_a_null_ActAs()
    {
        var act = () => JsonSerializer.Deserialize<SubmitterInfo>("{\"ActAs\":null}");

        act.Should().Throw<JsonException>().WithMessage("*SubmitterInfo*ActAs*null*");
    }

    [Fact]
    public void SubmitterInfo_rejects_a_non_object_token()
    {
        var act = () => JsonSerializer.Deserialize<SubmitterInfo>("[\"alice\"]");

        act.Should().Throw<JsonException>().WithMessage("*SubmitterInfo*StartArray*");
    }

    [Fact]
    public void SubmitterInfo_rejects_a_non_array_ActAs()
    {
        var act = () => JsonSerializer.Deserialize<SubmitterInfo>("{\"ActAs\":\"alice\"}");

        act.Should().Throw<JsonException>().WithMessage("*SubmitterInfo*ActAs*String*");
    }

    [Fact]
    public void SubmitterInfo_rejects_a_malformed_party_in_ActAs()
    {
        var act = () => JsonSerializer.Deserialize<SubmitterInfo>("{\"ActAs\":[\"\"]}");

        act.Should().Throw<JsonException>().WithMessage("*SubmitterInfo.ActAs*Party*empty*");
    }

    [Fact]
    public void SubmitterInfo_rejects_a_null_on_a_non_nullable_member()
    {
        var act = () => JsonSerializer.Deserialize<Submission>("{\"Submitter\":null}");

        act.Should().Throw<JsonException>().WithMessage("*SubmitterInfo*null*");
    }

    [Fact]
    public void A_nullable_SubmitterInfo_reads_a_JSON_null_as_absent()
    {
        var restored = JsonSerializer.Deserialize<OptionalSubmission>("{\"Submitter\":null}");

        restored!.Submitter.Should().BeNull();
    }

    [Fact]
    public void SubmitterInfo_refuses_to_serialize_an_uninitialized_value()
    {
        var act = () => JsonSerializer.Serialize(default(SubmitterInfo));

        act.Should().Throw<JsonException>().WithMessage("*uninitialized SubmitterInfo*");
    }

    [Fact]
    public void SubmitterInfo_follows_the_property_naming_policy_of_the_options()
    {
        var submitter = new SubmitterInfo(new Party("alice"), new HashSet<Party> { new("bob") });

        var json = JsonSerializer.Serialize(submitter, CamelCase);

        json.Should().Be("{\"actAs\":[\"alice\"],\"readAs\":[\"bob\"]}");
        JsonSerializer.Deserialize<SubmitterInfo>(json, CamelCase).Should().Be(submitter);
    }

    [Fact]
    public void SubmitterInfo_reads_a_differently_cased_property_when_the_options_ignore_case()
    {
        var restored = JsonSerializer.Deserialize<SubmitterInfo>("{\"actas\":[\"alice\"]}", CaseInsensitive);

        restored.Should().Be(new SubmitterInfo(new Party("alice")));
    }
}

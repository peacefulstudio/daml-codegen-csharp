using System.Text.Json;
using Daml.Runtime.Data;
using FluentAssertions;
using Xunit;

namespace Daml.Runtime.Tests;

public class PartyJsonTests
{
    private static readonly JsonSerializerOptions CaseInsensitiveOptions =
        new() { PropertyNameCaseInsensitive = true };

    private sealed record TemplatePayload(Party Operator, string MarketId);

    private sealed record OptionalPartyPayload(Party? Delegate, string Note);

    [Fact]
    public void Party_should_deserialize_from_json_string()
    {
        // PQS payloads encode parties as raw JSON strings, e.g.
        //   {"operator": "Platform::1220abcd..."}
        var json = "\"Alice::122012ab\"";

        var party = JsonSerializer.Deserialize<Party>(json);

        party.Id.Should().Be("Alice::122012ab");
    }

    [Fact]
    public void Party_should_serialize_as_json_string()
    {
        var party = new Party("Alice::122012ab");

        var json = JsonSerializer.Serialize(party);

        json.Should().Be("\"Alice::122012ab\"");
    }

    [Fact]
    public void Party_should_round_trip_inside_a_record_payload()
    {
        var json = "{\"operator\":\"Platform::1220abcd\",\"marketId\":\"BTC-USD\"}";

        var payload = JsonSerializer.Deserialize<TemplatePayload>(json, CaseInsensitiveOptions);

        payload.Should().NotBeNull();
        payload!.Operator.Id.Should().Be("Platform::1220abcd");
        payload.MarketId.Should().Be("BTC-USD");
    }

    [Fact]
    public void Party_should_throw_JsonException_when_token_is_not_a_string()
    {
        var json = "123";

        var act = () => JsonSerializer.Deserialize<Party>(json);

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void Party_should_throw_JsonException_when_string_is_empty()
    {
        var json = "\"\"";

        var act = () => JsonSerializer.Deserialize<Party>(json);

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void Party_should_throw_JsonException_when_string_is_whitespace()
    {
        var json = "\" \"";

        var act = () => JsonSerializer.Deserialize<Party>(json);

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void Party_should_throw_JsonException_when_serializing_uninitialized()
    {
        Party party = default;

        var act = () => JsonSerializer.Serialize(party);

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void Party_should_throw_JsonException_when_token_is_null()
    {
        var json = "null";

        var act = () => JsonSerializer.Deserialize<Party>(json);

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void Nullable_Party_should_deserialize_null_as_null()
    {
        var json = "{\"delegate\":null,\"note\":\"none\"}";

        var payload = JsonSerializer.Deserialize<OptionalPartyPayload>(json, CaseInsensitiveOptions);

        payload.Should().NotBeNull();
        payload!.Delegate.Should().BeNull();
        payload.Note.Should().Be("none");
    }

    [Fact]
    public void Nullable_Party_should_deserialize_string_as_party()
    {
        var json = "{\"delegate\":\"Alice::122012ab\",\"note\":\"set\"}";

        var payload = JsonSerializer.Deserialize<OptionalPartyPayload>(json, CaseInsensitiveOptions);

        payload.Should().NotBeNull();
        payload!.Delegate.Should().NotBeNull();
        payload!.Delegate!.Value.Id.Should().Be("Alice::122012ab");
    }
}

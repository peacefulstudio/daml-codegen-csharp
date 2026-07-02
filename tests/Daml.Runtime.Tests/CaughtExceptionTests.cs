// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using AwesomeAssertions;
using Xunit;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;

namespace Daml.Runtime.Tests;

public class CaughtExceptionTests
{
    [Fact]
    public void CaughtExceptions_defaults_to_empty_when_not_set()
    {
        var exercised = MakeExercisedEvent();

        exercised.CaughtExceptions.Should().NotBeNull();
        exercised.CaughtExceptions.Should().BeEmpty();
    }

    [Fact]
    public void CaughtExceptions_round_trips_a_single_caught_exception()
    {
        var caught = new CaughtException(
            ErrorId: "Acme.Errors:InsufficientFunds",
            Message: "not enough funds",
            Metadata: new Dictionary<string, string> { ["required"] = "100" });

        var exercised = MakeExercisedEvent() with { CaughtExceptions = [caught] };

        exercised.CaughtExceptions.Should().ContainSingle().Which.Should().Be(caught);
        exercised.CaughtExceptions[0].ErrorId.Should().Be("Acme.Errors:InsufficientFunds");
        exercised.CaughtExceptions[0].Message.Should().Be("not enough funds");
        exercised.CaughtExceptions[0].Metadata.Should().ContainKey("required").WhoseValue.Should().Be("100");
    }

    [Fact]
    public void CaughtExceptions_round_trips_multiple_caught_exceptions()
    {
        var first = new CaughtException("Acme.Errors:InsufficientFunds", "not enough funds", new Dictionary<string, string>());
        var second = new CaughtException("Acme.Errors:Expired", "offer expired", new Dictionary<string, string>());

        var exercised = MakeExercisedEvent() with { CaughtExceptions = [first, second] };

        exercised.CaughtExceptions.Should().HaveCount(2);
        exercised.CaughtExceptions[0].Should().Be(first);
        exercised.CaughtExceptions[1].Should().Be(second);
    }

    private static ExercisedEvent MakeExercisedEvent() => new(
        ContractId: "00alice",
        TemplateId: new RuntimeIdentifier("test-pkg", "Acme.Foo", "FooBar"),
        InterfaceId: null,
        ChoiceName: "DoThing",
        ChoiceArgument: DamlUnit.Instance,
        ExerciseResult: DamlUnit.Instance,
        Consuming: false,
        ActingParties: [new Party("alice")],
        WitnessParties: [new Party("alice")]);
}

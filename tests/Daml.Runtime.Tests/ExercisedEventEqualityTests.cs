// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using AwesomeAssertions;
using Xunit;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;

namespace Daml.Runtime.Tests;

/// <summary>
/// Pins that <see cref="ExercisedEvent"/> and <see cref="CaughtException"/> compare by
/// content rather than by the identity of their collection members, which is what makes
/// <see cref="TransactionResult"/> equality structural all the way down rather than only at
/// the list level.
/// </summary>
public class ExercisedEventEqualityTests
{
    [Fact]
    public void Two_independently_built_events_describing_the_same_exercise_are_equal()
    {
        var first = MakeExercised();
        var second = MakeExercised();

        first.ActingParties.Should().NotBeSameAs(second.ActingParties);
        first.WitnessParties.Should().NotBeSameAs(second.WitnessParties);
        first.Should().Be(second);
        first.GetHashCode().Should().Be(second.GetHashCode());
    }

    [Fact]
    public void Events_with_different_acting_parties_are_not_equal()
    {
        var first = MakeExercised(actingParties: [new Party("alice")]);
        var second = MakeExercised(actingParties: [new Party("bob")]);

        first.Should().NotBe(second);
    }

    [Fact]
    public void Events_with_acting_parties_in_a_different_order_are_not_equal()
    {
        var first = MakeExercised(actingParties: [new Party("alice"), new Party("bob")]);
        var second = MakeExercised(actingParties: [new Party("bob"), new Party("alice")]);

        first.Should().NotBe(second);
    }

    [Fact]
    public void Acting_and_witness_parties_are_hashed_with_a_length_separator()
    {
        var split = MakeExercised(
            actingParties: [new Party("alice")],
            witnessParties: [new Party("bob")]);
        var merged = MakeExercised(
            actingParties: [new Party("alice"), new Party("bob")],
            witnessParties: []);

        split.Should().NotBe(merged);
        split.GetHashCode().Should().NotBe(merged.GetHashCode());
    }

    [Fact]
    public void Events_with_equal_caught_exceptions_built_separately_are_equal()
    {
        var first = MakeExercised() with { CaughtExceptions = [MakeCaught()] };
        var second = MakeExercised() with { CaughtExceptions = [MakeCaught()] };

        first.CaughtExceptions[0].Should().NotBeSameAs(second.CaughtExceptions[0]);
        first.Should().Be(second);
        first.GetHashCode().Should().Be(second.GetHashCode());
    }

    [Fact]
    public void Events_with_different_caught_exceptions_are_not_equal()
    {
        var first = MakeExercised() with { CaughtExceptions = [MakeCaught()] };
        var second = MakeExercised() with { CaughtExceptions = [MakeCaught(errorId: "Acme.Errors:Expired")] };

        first.Should().NotBe(second);
    }

    [Fact]
    public void Caught_exceptions_compare_metadata_independently_of_insertion_order()
    {
        var first = MakeCaught(metadata: new Dictionary<string, string>
        {
            ["required"] = "100",
            ["available"] = "40",
        });
        var second = MakeCaught(metadata: new Dictionary<string, string>
        {
            ["available"] = "40",
            ["required"] = "100",
        });

        first.Should().Be(second);
        first.GetHashCode().Should().Be(second.GetHashCode());
    }

    [Fact]
    public void Caught_exceptions_with_different_metadata_values_are_not_equal()
    {
        var first = MakeCaught(metadata: new Dictionary<string, string> { ["required"] = "100" });
        var second = MakeCaught(metadata: new Dictionary<string, string> { ["required"] = "200" });

        first.Should().NotBe(second);
    }

    [Fact]
    public void Caught_exceptions_with_a_metadata_key_the_other_lacks_are_not_equal()
    {
        var first = MakeCaught(metadata: new Dictionary<string, string> { ["required"] = "100" });
        var second = MakeCaught(metadata: new Dictionary<string, string> { ["available"] = "100" });

        first.Should().NotBe(second);
    }

    private static ExercisedEvent MakeExercised(
        IReadOnlyList<Party>? actingParties = null,
        IReadOnlyList<Party>? witnessParties = null) =>
        new(
            ContractId: "00c",
            TemplateId: new RuntimeIdentifier("test-pkg", "Acme.Foo", "FooBar"),
            InterfaceId: null,
            ChoiceName: "DoThing",
            ChoiceArgument: DamlUnit.Instance,
            ExerciseResult: DamlUnit.Instance,
            Consuming: true,
            ActingParties: actingParties ?? [new Party("alice")],
            WitnessParties: witnessParties ?? [new Party("alice")]);

    private static CaughtException MakeCaught(
        string errorId = "Acme.Errors:InsufficientFunds",
        IReadOnlyDictionary<string, string>? metadata = null) =>
        new(
            ErrorId: errorId,
            Message: "not enough funds",
            Metadata: metadata ?? new Dictionary<string, string> { ["required"] = "100" });
}

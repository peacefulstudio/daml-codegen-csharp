// Copyright (c) 2026 Peaceful Studio OÜ. All rights reserved.

using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using FluentAssertions;
using Xunit;
using RuntimeIdentifier = Daml.Runtime.Data.Identifier;

namespace Daml.Runtime.Tests;

public class ExerciseOutcomeTests
{
    [Fact]
    public void One_carries_contract_id_payload()
    {
        var cid = new ContractId<FooBar>("00abc");
        var outcome = new ExerciseOutcome<ContractId<FooBar>>.One(cid);

        outcome.Result.Should().BeSameAs(cid);
    }

    [Fact]
    public void One_carries_transaction_result_payload()
    {
        var result = new TransactionResult("u1", 1L, [], []);
        var outcome = new ExerciseOutcome<TransactionResult>.One(result);

        outcome.Result.Should().BeSameAs(result);
    }

    [Fact]
    public void One_can_carry_a_decimal_payload()
    {
        // The outcome type imposes no constraint on T — choice results that aren't
        // template-typed (e.g. a Decimal returned from a `nonconsuming` choice) round-trip.
        var outcome = new ExerciseOutcome<decimal>.One(42.5m);

        outcome.Result.Should().Be(42.5m);
    }

    [Fact]
    public void One_can_carry_an_arbitrary_record_payload()
    {
        // Composite Daml choice results are emitted as plain C# records by codegen;
        // the outcome wrapper accepts any T.
        var result = new SwapChoiceResult(new ContractId<FooBar>("00agreement"), new ContractId<FooBar>("00record"));
        var outcome = new ExerciseOutcome<SwapChoiceResult>.One(result);

        outcome.Result.Should().Be(result);
    }

    [Fact]
    public void None_constructs_without_state()
    {
        var outcome = new ExerciseOutcome<ContractId<FooBar>>.None();

        outcome.Should().NotBeNull();
    }

    [Fact]
    public void Many_carries_count_and_contract_ids()
    {
        var ids = new[] { "00a", "00b", "00c" };
        var outcome = new ExerciseOutcome<ContractId<FooBar>>.Many(3, ids);

        outcome.Count.Should().Be(3);
        outcome.ContractIds.Should().Equal(ids);
    }

    [Fact]
    public void DamlError_carries_full_payload()
    {
        var metadata = new Dictionary<string, string> { ["category"] = "InvalidGivenCurrentSystemStateOther" };
        var outcome = new ExerciseOutcome<ContractId<FooBar>>.DamlError(
            DamlErrorCategory.InvalidGivenCurrentSystemStateOther,
            "SAMPLE_SWAP_ALREADY_EXECUTED",
            "swap already executed",
            metadata);

        outcome.Category.Should().Be(DamlErrorCategory.InvalidGivenCurrentSystemStateOther);
        outcome.ErrorId.Should().Be("SAMPLE_SWAP_ALREADY_EXECUTED");
        outcome.Message.Should().Be("swap already executed");
        outcome.Metadata.Should().Equal(metadata);
    }

    [Fact]
    public void DamlError_works_for_transaction_result_payload()
    {
        var metadata = new Dictionary<string, string> { ["category"] = "ContentionOnSharedResources" };
        var outcome = new ExerciseOutcome<TransactionResult>.DamlError(
            DamlErrorCategory.ContentionOnSharedResources,
            "CONTRACT_NOT_FOUND",
            "contract not found",
            metadata);

        outcome.Category.Should().Be(DamlErrorCategory.ContentionOnSharedResources);
        outcome.ErrorId.Should().Be("CONTRACT_NOT_FOUND");
        outcome.Message.Should().Be("contract not found");
        outcome.Metadata.Should().Equal(metadata);
    }

    [Fact]
    public void InfraError_carries_status_code_and_message()
    {
        // StatusCode is `int` (cast `(int)Grpc.Core.StatusCode.DeadlineExceeded` at the
        // gRPC client construction site) so this type stays free of any transport-library dep.
        var outcome = new ExerciseOutcome<ContractId<FooBar>>.InfraError(StatusCodes.DeadlineExceeded, "deadline");

        outcome.StatusCode.Should().Be(StatusCodes.DeadlineExceeded);
        outcome.Message.Should().Be("deadline");
    }

    [Fact]
    public void InfraError_works_for_transaction_result_payload()
    {
        var outcome = new ExerciseOutcome<TransactionResult>.InfraError(StatusCodes.Unavailable, "network down");

        outcome.StatusCode.Should().Be(StatusCodes.Unavailable);
        outcome.Message.Should().Be("network down");
    }

    [Fact]
    public void Variants_should_be_distinguishable_via_pattern_match()
    {
        ExerciseOutcome<ContractId<FooBar>>[] outcomes =
        [
            new ExerciseOutcome<ContractId<FooBar>>.One(new ContractId<FooBar>("c")),
            new ExerciseOutcome<ContractId<FooBar>>.None(),
            new ExerciseOutcome<ContractId<FooBar>>.Many(2, ["c1", "c2"]),
            new ExerciseOutcome<ContractId<FooBar>>.DamlError(DamlErrorCategory.Unknown, "X", "x", new Dictionary<string, string>()),
            new ExerciseOutcome<ContractId<FooBar>>.InfraError(StatusCodes.Unavailable, "u"),
        ];

        var seen = outcomes.Select(o => o switch
        {
            ExerciseOutcome<ContractId<FooBar>>.One => "one",
            ExerciseOutcome<ContractId<FooBar>>.None => "none",
            ExerciseOutcome<ContractId<FooBar>>.Many => "many",
            ExerciseOutcome<ContractId<FooBar>>.DamlError => "daml-err",
            ExerciseOutcome<ContractId<FooBar>>.InfraError => "infra-err",
            _ => "other",
        }).ToList();

        seen.Should().Equal("one", "none", "many", "daml-err", "infra-err");
    }

    /// <summary>
    /// Mirrors a subset of <c>Grpc.Core.StatusCode</c> values, kept as plain ints so this
    /// test project doesn't take a gRPC dep just to construct an <c>InfraError</c>. Real
    /// callers cast <c>(int)Grpc.Core.StatusCode.X</c> at the construction site.
    /// </summary>
    private static class StatusCodes
    {
        public const int Unavailable = 14;
        public const int DeadlineExceeded = 4;
    }

    private sealed record SwapChoiceResult(
        ContractId<FooBar> Agreement,
        ContractId<FooBar> SwapRecord);

    private sealed record FooBar(string Owner) : ITemplate
    {
        public static RuntimeIdentifier TemplateId { get; } = new("test-pkg", "Sample.Foo", "FooBar");
        public static string PackageId => "test-pkg";
        public static string PackageName => "test-package";
        public static Version PackageVersion { get; } = new(0, 1, 0);

        public DamlRecord ToRecord() => DamlRecord.Create(
            DamlField.Create("owner", new DamlParty(Owner)));
    }
}

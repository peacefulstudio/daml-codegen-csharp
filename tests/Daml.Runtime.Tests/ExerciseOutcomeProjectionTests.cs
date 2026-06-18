// Copyright (c) 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using FluentAssertions;
using Xunit;

namespace Daml.Runtime.Tests;

/// <summary>
/// End-to-end tests for the projection logic the codegen emits — exercised here against
/// a hand-rolled fixture that mirrors the shape <c>CSharpCodeGenerator</c> emits for
/// a multi-create choice. Verifies cardinality validation
/// (<see cref="ExerciseOutcome{T}.None"/> / <see cref="ExerciseOutcome{T}.Many"/> /
/// <see cref="ExerciseOutcome{T}.One"/>) on the upstream outcome type directly,
/// without going through the codegen pipeline.
/// </summary>
public class ExerciseOutcomeProjectionTests
{
    /// <summary>Stand-in for a Daml template (the choice's first created type).</summary>
    private sealed record Agreement : ITemplate
    {
        public static Identifier TemplateId => new("pkg-1", "Acme.Agreements", "Agreement");
        public static string PackageId => "pkg-1";
        public static string PackageName => "peaceful-acme-agreements";
        public static Version PackageVersion => new(0, 0, 1);
        public DamlRecord ToRecord() => DamlRecord.Create();
        public static Agreement FromRecord(DamlRecord record) => new();
    }

    /// <summary>Stand-in for a Daml template (the choice's second created type).</summary>
    private sealed record SwapRecord : ITemplate
    {
        public static Identifier TemplateId => new("pkg-1", "Acme.Agreements", "SwapRecord");
        public static string PackageId => "pkg-1";
        public static string PackageName => "peaceful-acme-agreements";
        public static Version PackageVersion => new(0, 0, 1);
        public DamlRecord ToRecord() => DamlRecord.Create();
        public static SwapRecord FromRecord(DamlRecord record) => new();
    }

    /// <summary>Stand-in for the choice's optional third created type.</summary>
    private sealed record AgreementRecord : ITemplate
    {
        public static Identifier TemplateId => new("pkg-1", "Acme.Agreements", "AgreementRecord");
        public static string PackageId => "pkg-1";
        public static string PackageName => "peaceful-acme-agreements";
        public static Version PackageVersion => new(0, 0, 1);
        public DamlRecord ToRecord() => DamlRecord.Create();
        public static AgreementRecord FromRecord(DamlRecord record) => new();
    }

    /// <summary>
    /// Mirrors the shape codegen emits for
    /// <c>ExecuteSwap : (ContractId Agreement, ContractId SwapRecord, Optional (ContractId AgreementRecord))</c>.
    /// The <c>FromCreatedContracts</c> body is hand-written here but is the same
    /// algorithm <c>CSharpCodeGenerator</c> emits — this test pins it.
    /// </summary>
    private sealed record ExecuteSwapResult(
        ContractId<Agreement> Agreement,
        ContractId<SwapRecord> SwapRecord,
        ContractId<AgreementRecord>? AgreementRecord)
    {
        public static ExerciseOutcome<ExecuteSwapResult> FromCreatedContracts(IEnumerable<CreatedContract> created)
        {
            ArgumentNullException.ThrowIfNull(created);
            var matches0 = new List<string>();
            var matches1 = new List<string>();
            var matches2 = new List<string>();

            foreach (var item in created)
            {
                if (string.Equals(item.TemplateId.ModuleName, Tests.ExerciseOutcomeProjectionTests.Agreement.TemplateId.ModuleName, StringComparison.Ordinal)
                    && string.Equals(item.TemplateId.EntityName, Tests.ExerciseOutcomeProjectionTests.Agreement.TemplateId.EntityName, StringComparison.Ordinal))
                {
                    matches0.Add(item.ContractId);
                }
                else if (string.Equals(item.TemplateId.ModuleName, Tests.ExerciseOutcomeProjectionTests.SwapRecord.TemplateId.ModuleName, StringComparison.Ordinal)
                    && string.Equals(item.TemplateId.EntityName, Tests.ExerciseOutcomeProjectionTests.SwapRecord.TemplateId.EntityName, StringComparison.Ordinal))
                {
                    matches1.Add(item.ContractId);
                }
                else if (string.Equals(item.TemplateId.ModuleName, Tests.ExerciseOutcomeProjectionTests.AgreementRecord.TemplateId.ModuleName, StringComparison.Ordinal)
                    && string.Equals(item.TemplateId.EntityName, Tests.ExerciseOutcomeProjectionTests.AgreementRecord.TemplateId.EntityName, StringComparison.Ordinal))
                {
                    matches2.Add(item.ContractId);
                }
            }

            if (matches0.Count == 0)
            {
                return new ExerciseOutcome<ExecuteSwapResult>.None();
            }
            if (matches0.Count > 1)
            {
                return new ExerciseOutcome<ExecuteSwapResult>.Many(matches0.Count, matches0);
            }
            if (matches1.Count == 0)
            {
                return new ExerciseOutcome<ExecuteSwapResult>.None();
            }
            if (matches1.Count > 1)
            {
                return new ExerciseOutcome<ExecuteSwapResult>.Many(matches1.Count, matches1);
            }
            if (matches2.Count > 1)
            {
                return new ExerciseOutcome<ExecuteSwapResult>.Many(matches2.Count, matches2);
            }

            return new ExerciseOutcome<ExecuteSwapResult>.One(new ExecuteSwapResult(
                Agreement: new ContractId<Agreement>(matches0[0]),
                SwapRecord: new ContractId<SwapRecord>(matches1[0]),
                AgreementRecord: matches2.Count == 1 ? new ContractId<AgreementRecord>(matches2[0]) : null));
        }
    }

    private static CreatedContract Created<T>(string contractId) where T : ITemplate =>
        new(contractId, T.TemplateId, "{}");

    [Fact]
    public void FromCreatedContracts_should_return_One_when_all_required_slots_match()
    {
        var refs = new[]
        {
            Created<Agreement>("agree-cid-1"),
            Created<SwapRecord>("swap-cid-1"),
            Created<AgreementRecord>("rec-cid-1"),
        };

        var outcome = ExecuteSwapResult.FromCreatedContracts(refs);

        outcome.Should().BeOfType<ExerciseOutcome<ExecuteSwapResult>.One>();
        var one = (ExerciseOutcome<ExecuteSwapResult>.One)outcome;
        one.Result.Agreement.Value.Should().Be("agree-cid-1");
        one.Result.SwapRecord.Value.Should().Be("swap-cid-1");
        one.Result.AgreementRecord.Should().NotBeNull();
        one.Result.AgreementRecord!.Value.Should().Be("rec-cid-1");
    }

    [Fact]
    public void FromCreatedContracts_should_return_One_when_optional_slot_is_absent()
    {
        var refs = new[]
        {
            Created<Agreement>("agree-cid-1"),
            Created<SwapRecord>("swap-cid-1"),
        };

        var outcome = ExecuteSwapResult.FromCreatedContracts(refs);

        outcome.Should().BeOfType<ExerciseOutcome<ExecuteSwapResult>.One>();
        var one = (ExerciseOutcome<ExecuteSwapResult>.One)outcome;
        one.Result.AgreementRecord.Should().BeNull();
    }

    [Fact]
    public void FromCreatedContracts_should_return_None_when_required_slot_missing()
    {
        // Missing the Agreement slot.
        var refs = new[]
        {
            Created<SwapRecord>("swap-cid-1"),
        };

        var outcome = ExecuteSwapResult.FromCreatedContracts(refs);

        outcome.Should().BeOfType<ExerciseOutcome<ExecuteSwapResult>.None>();
    }

    [Fact]
    public void FromCreatedContracts_should_return_Many_when_required_slot_overshoots()
    {
        // Two Agreements created — single-cardinality slot overshoots.
        var refs = new[]
        {
            Created<Agreement>("agree-cid-1"),
            Created<Agreement>("agree-cid-2"),
            Created<SwapRecord>("swap-cid-1"),
        };

        var outcome = ExecuteSwapResult.FromCreatedContracts(refs);

        outcome.Should().BeOfType<ExerciseOutcome<ExecuteSwapResult>.Many>();
        var many = (ExerciseOutcome<ExecuteSwapResult>.Many)outcome;
        many.Count.Should().Be(2);
        many.ContractIds.Should().HaveCount(2);
        many.ContractIds.Should().Contain("agree-cid-1");
        many.ContractIds.Should().Contain("agree-cid-2");
    }

    [Fact]
    public void FromCreatedContracts_should_return_Many_when_optional_slot_has_more_than_one()
    {
        // Two AgreementRecords — Optional cardinality caps at 1.
        var refs = new[]
        {
            Created<Agreement>("agree-cid-1"),
            Created<SwapRecord>("swap-cid-1"),
            Created<AgreementRecord>("rec-cid-1"),
            Created<AgreementRecord>("rec-cid-2"),
        };

        var outcome = ExecuteSwapResult.FromCreatedContracts(refs);

        outcome.Should().BeOfType<ExerciseOutcome<ExecuteSwapResult>.Many>();
        var many = (ExerciseOutcome<ExecuteSwapResult>.Many)outcome;
        many.Count.Should().Be(2);
    }

    [Fact]
    public void FromCreatedContracts_should_ignore_unrelated_created_contracts()
    {
        // Extra contract of a foreign template should not break projection.
        var foreignId = new Identifier("other-pkg", "Other.Module", "Foo");
        var refs = new[]
        {
            Created<Agreement>("agree-cid-1"),
            Created<SwapRecord>("swap-cid-1"),
            new CreatedContract("foreign-cid", foreignId, "{}"),
        };

        var outcome = ExecuteSwapResult.FromCreatedContracts(refs);

        outcome.Should().BeOfType<ExerciseOutcome<ExecuteSwapResult>.One>();
    }

    [Fact]
    public void FromCreatedContracts_should_throw_on_null_input()
    {
        var act = () => ExecuteSwapResult.FromCreatedContracts(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    /// <summary>
    /// A choice whose return type is <c>(ContractId Half, ContractId Half)</c> — the
    /// duplicate-template case. The hand-rolled projector here mirrors the shape codegen
    /// emits for this case (per-template bucket, distribute across slots in declaration
    /// order, surface leftover into the last slot so cardinality validation can flag it).
    /// </summary>
    private sealed record Half : ITemplate
    {
        public static Identifier TemplateId => new("pkg-1", "Acme.Splitters", "Half");
        public static string PackageId => "pkg-1";
        public static string PackageName => "peaceful-acme-splitters";
        public static Version PackageVersion => new(0, 0, 1);
        public DamlRecord ToRecord() => DamlRecord.Create();
        public static Half FromRecord(DamlRecord record) => new();
    }

    private sealed record SplitResult(
        ContractId<Half> HalfA,
        ContractId<Half> HalfB)
    {
        public static ExerciseOutcome<SplitResult> FromCreatedContracts(IEnumerable<CreatedContract> created)
        {
            ArgumentNullException.ThrowIfNull(created);

            // One bucket per unique template. Both slots fan out from this bucket below.
            var templateMatches0 = new List<string>();
            foreach (var item in created)
            {
                if (string.Equals(item.TemplateId.ModuleName, Tests.ExerciseOutcomeProjectionTests.Half.TemplateId.ModuleName, StringComparison.Ordinal)
                    && string.Equals(item.TemplateId.EntityName, Tests.ExerciseOutcomeProjectionTests.Half.TemplateId.EntityName, StringComparison.Ordinal))
                {
                    templateMatches0.Add(item.ContractId);
                }
            }

            // Distribute across slots in declaration order. Single/Optional slots take
            // one each; leftover lands in the last slot for the group so the cardinality
            // validator surfaces it as Many.
            var matches0 = new List<string>();
            var matches1 = new List<string>();
            var templateMatchIndex0 = 0;
            if (templateMatchIndex0 < templateMatches0.Count)
            {
                matches0.Add(templateMatches0[templateMatchIndex0]);
                templateMatchIndex0++;
            }
            if (templateMatchIndex0 < templateMatches0.Count)
            {
                matches1.Add(templateMatches0[templateMatchIndex0]);
                templateMatchIndex0++;
            }
            if (templateMatchIndex0 < templateMatches0.Count)
            {
                while (templateMatchIndex0 < templateMatches0.Count)
                {
                    matches1.Add(templateMatches0[templateMatchIndex0]);
                    templateMatchIndex0++;
                }
            }

            if (matches0.Count == 0)
            {
                return new ExerciseOutcome<SplitResult>.None();
            }
            if (matches0.Count > 1)
            {
                return new ExerciseOutcome<SplitResult>.Many(matches0.Count, matches0);
            }
            if (matches1.Count == 0)
            {
                return new ExerciseOutcome<SplitResult>.None();
            }
            if (matches1.Count > 1)
            {
                return new ExerciseOutcome<SplitResult>.Many(matches1.Count, matches1);
            }

            return new ExerciseOutcome<SplitResult>.One(new SplitResult(
                HalfA: new ContractId<Half>(matches0[0]),
                HalfB: new ContractId<Half>(matches1[0])));
        }
    }

    [Fact]
    public void FromCreatedContracts_should_distribute_duplicate_template_across_slots()
    {
        // Two Half contracts created — one to each slot. Pre-fix, the if/else if chain
        // sent both into matches0 and left matches1 empty, so this would have returned
        // .Many on slot 0 — silently broken for duplicate-template choice returns.
        var refs = new[]
        {
            Created<Half>("half-cid-1"),
            Created<Half>("half-cid-2"),
        };

        var outcome = SplitResult.FromCreatedContracts(refs);

        outcome.Should().BeOfType<ExerciseOutcome<SplitResult>.One>();
        var one = (ExerciseOutcome<SplitResult>.One)outcome;
        one.Result.HalfA.Value.Should().Be("half-cid-1");
        one.Result.HalfB.Value.Should().Be("half-cid-2");
    }

    [Fact]
    public void FromCreatedContracts_should_return_None_when_duplicate_template_undershoots()
    {
        // Only one Half — second slot stays empty, projector should produce None.
        var refs = new[]
        {
            Created<Half>("half-cid-1"),
        };

        var outcome = SplitResult.FromCreatedContracts(refs);

        outcome.Should().BeOfType<ExerciseOutcome<SplitResult>.None>();
    }

    [Fact]
    public void FromCreatedContracts_should_return_Many_when_duplicate_template_overshoots()
    {
        // Three Half contracts where only two slots exist — leftover surfaces in the
        // last slot's bucket and trips its single-cardinality > 1 check.
        var refs = new[]
        {
            Created<Half>("half-cid-1"),
            Created<Half>("half-cid-2"),
            Created<Half>("half-cid-3"),
        };

        var outcome = SplitResult.FromCreatedContracts(refs);

        outcome.Should().BeOfType<ExerciseOutcome<SplitResult>.Many>();
        var many = (ExerciseOutcome<SplitResult>.Many)outcome;
        many.Count.Should().Be(2);
        many.ContractIds.Should().Contain("half-cid-2");
        many.ContractIds.Should().Contain("half-cid-3");
    }
}

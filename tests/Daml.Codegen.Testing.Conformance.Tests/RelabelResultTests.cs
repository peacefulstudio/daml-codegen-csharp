// Copyright (c) 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using FluentAssertions;
using Daml.Codegen.Testing.Conformance.Richtypes;
using Xunit;

namespace Daml.Codegen.Testing.Conformance.Tests;

public class RelabelResultTests
{
    private static CreatedContract RichRecordCreated(string contractId) =>
        new(contractId, RichRecord.TemplateId, "{}");

    [Fact]
    public void from_created_contracts_returns_one_with_the_typed_id_on_a_single_match()
    {
        var outcome = RelabelResult.FromCreatedContracts(new[] { RichRecordCreated("rich-cid-1") });

        outcome.Should().BeOfType<ExerciseOutcome<RelabelResult>.One>();
        ((ExerciseOutcome<RelabelResult>.One)outcome).Result.RichRecord.Value.Should().Be("rich-cid-1");
    }

    [Fact]
    public void from_created_contracts_returns_none_when_the_required_template_is_absent()
    {
        var foreign = new CreatedContract("foreign-cid", new Identifier("other", "Other.Mod", "Foo"), "{}");

        var outcome = RelabelResult.FromCreatedContracts(new[] { foreign });

        outcome.Should().BeOfType<ExerciseOutcome<RelabelResult>.None>();
    }

    [Fact]
    public void from_created_contracts_returns_many_when_the_single_slot_overshoots()
    {
        var outcome = RelabelResult.FromCreatedContracts(new[]
        {
            RichRecordCreated("rich-cid-1"),
            RichRecordCreated("rich-cid-2"),
        });

        outcome.Should().BeOfType<ExerciseOutcome<RelabelResult>.Many>();
        var many = (ExerciseOutcome<RelabelResult>.Many)outcome;
        many.Count.Should().Be(2);
        many.ContractIds.Should().Equal("rich-cid-1", "rich-cid-2");
    }

    [Fact]
    public void from_created_contracts_matches_on_module_and_entity_ignoring_package_id()
    {
        var upgraded = new CreatedContract(
            "rich-cid-1",
            RichRecord.TemplateId with { PackageId = "a-different-package-hash" },
            "{}");

        var outcome = RelabelResult.FromCreatedContracts(new[] { upgraded });

        outcome.Should().BeOfType<ExerciseOutcome<RelabelResult>.One>();
    }

    [Fact]
    public void from_created_contracts_throws_on_null_input()
    {
        var act = () => RelabelResult.FromCreatedContracts(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}

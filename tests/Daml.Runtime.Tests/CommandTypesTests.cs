// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using AwesomeAssertions;
using Xunit;

namespace Daml.Runtime.Tests;

public class CommandTypesTests
{
    // Test template for Command testing
    private sealed record TestTemplate(Party Owner, long Amount) : ITemplate
    {
        public static Identifier TemplateId => new("test-package", "Test.Module", "TestTemplate");
        public static string PackageId => "test-package";
        public static string PackageName => "test-package-name";
        public static Version PackageVersion => new(1, 0, 0);
        public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);

        public DamlRecord ToRecord() => DamlRecord.Create(
            DamlField.Create("owner", Owner.ToDamlValue()),
            DamlField.Create("amount", new DamlInt64(Amount)));

        public static TestTemplate FromRecord(DamlRecord record) =>
            new(Party.FromDamlValue(record.GetRequiredField("owner").As<DamlParty>()),
                record.GetRequiredField("amount").As<DamlInt64>().Value);
    }

    private interface TestInterfaceMarker : IDamlInterface
    {
        static Identifier IDamlInterface.InterfaceId => new("test-package", "Test.Module", "TestInterfaceMarker");
        static string IDamlInterface.PackageId => "test-package";
        static string IDamlInterface.PackageName => "test-package-name";
        static Version IDamlInterface.PackageVersion => new(1, 0, 0);
        static DamlTypeDescriptor global::Daml.Runtime.IDamlType.DamlTypeId =>
            new(new Identifier("test-package", "Test.Module", "TestInterfaceMarker"), DamlTypeKind.Interface, "test-package-name");
    }

    [Fact]
    public void CreateCommand_should_have_correct_command_type()
    {
        // Arrange
        var templateId = new Identifier("pkg", "Module", "Template");
        var args = DamlRecord.Create(DamlField.Create("field", new DamlText("value")));

        // Act
        var command = new CreateCommand(templateId, args);

        // Assert
        command.CommandType.Should().Be("Create");
        command.TemplateId.Should().Be(templateId);
        command.CreateArguments.Should().Be(args);
    }

    [Fact]
    public void CreateCommand_For_should_create_command_from_template()
    {
        // Arrange
        var template = new TestTemplate(new Party("Alice"), 100);

        // Act
        var command = CreateCommand.For(template);

        // Assert
        command.CommandType.Should().Be("Create");
        command.TemplateId.Should().Be(TestTemplate.TemplateId);
        command.CreateArguments.GetField("owner")!.As<DamlParty>().Value.Should().Be("Alice");
        command.CreateArguments.GetField("amount")!.As<DamlInt64>().Value.Should().Be(100);
    }

    [Fact]
    public void ExerciseCommand_should_have_correct_command_type()
    {
        // Arrange
        var templateId = new Identifier("pkg", "Module", "Template");
        var contractId = new ContractId<TestTemplate>("contract-id-123");
        var choice = new ChoiceName("Transfer");
        var arg = new DamlText("argument");

        // Act
        var command = new ExerciseCommand(templateId, contractId, choice, arg);

        // Assert
        command.CommandType.Should().Be("Exercise");
        command.TemplateId.Should().Be(templateId);
        command.ContractId.Value.Should().Be("contract-id-123");
        command.Choice.Should().Be(choice);
        command.ChoiceArgument.Should().Be(arg);
    }

    [Fact]
    public void ExerciseCommand_should_carry_a_typed_choice_name_that_unwraps_to_its_wire_string()
    {
        var templateId = new Identifier("pkg", "Module", "Template");
        var arg = new DamlText("argument");

        var command = new ExerciseCommand(
            templateId,
            new ContractId<TestTemplate>("contract-id-123"),
            new ChoiceName("Transfer"),
            arg);

        command.Choice.Value.Should().Be("Transfer");
    }

    [Fact]
    public void ExerciseCommand_For_should_create_command_from_contract_id()
    {
        // Arrange
        var contractId = new ContractId<TestTemplate>("contract-123");
        var choice = new ChoiceName("Archive");
        var arg = DamlUnit.Instance;

        // Act
        var command = ExerciseCommand.For(contractId, choice, arg);

        // Assert
        command.CommandType.Should().Be("Exercise");
        command.TemplateId.Should().Be(TestTemplate.TemplateId);
        command.ContractId.Value.Should().Be("contract-123");
        command.Choice.Should().Be(choice);
        command.ChoiceArgument.Should().Be(DamlUnit.Instance);
    }

    [Fact]
    public void ExerciseCommand_For_rejects_a_null_contract_id()
    {
        var act = () => ExerciseCommand.For<TestTemplate>(null!, new ChoiceName("Archive"), DamlUnit.Instance);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ExerciseCommand_ForInterface_rejects_a_null_contract_id()
    {
        var act = () => ExerciseCommand.ForInterface<TestInterfaceMarker>(null!, new ChoiceName("Transfer"), DamlUnit.Instance);

        act.Should().Throw<ArgumentNullException>();
    }

    private sealed record ArchivableContract(ContractId<TestTemplate> ContractId) : IExercises<TestTemplate>;

    [Fact]
    public void ExerciseArchive_encodes_the_argument_as_an_empty_record_not_unit()
    {
        IExercises<TestTemplate> exercisable = new ArchivableContract(new ContractId<TestTemplate>("contract-id-123"));

        var command = exercisable.ExerciseArchive();

        command.Choice.Value.Should().Be("Archive");
        command.ChoiceArgument.Should().BeOfType<DamlRecord>()
            .Which.Fields.Should().BeEmpty();
        command.ChoiceArgument.Should().NotBeOfType<DamlUnit>();
    }

    [Fact]
    public void ExerciseByKeyCommand_should_have_correct_command_type()
    {
        // Arrange
        var templateId = new Identifier("pkg", "Module", "Template");
        var key = new DamlText("contract-key");
        var choice = new ChoiceName("Transfer");
        var arg = new DamlInt64(100);

        // Act
        var command = new ExerciseByKeyCommand(templateId, key, choice, arg);

        // Assert
        command.CommandType.Should().Be("ExerciseByKey");
        command.TemplateId.Should().Be(templateId);
        command.ContractKey.Should().Be(key);
        command.Choice.Should().Be(choice);
        command.ChoiceArgument.Should().Be(arg);
    }

    [Fact]
    public void CreateAndExerciseCommand_should_have_correct_command_type()
    {
        // Arrange
        var templateId = new Identifier("pkg", "Module", "Template");
        var createArgs = DamlRecord.Create(DamlField.Create("field", new DamlText("value")));
        var choice = new ChoiceName("Archive");
        var choiceArg = DamlUnit.Instance;

        // Act
        var command = new CreateAndExerciseCommand(templateId, createArgs, choice, choiceArg);

        // Assert
        command.CommandType.Should().Be("CreateAndExercise");
        command.TemplateId.Should().Be(templateId);
        command.CreateArguments.Should().Be(createArgs);
        command.Choice.Should().Be(choice);
        command.ChoiceArgument.Should().Be(choiceArg);
    }

    [Fact]
    public void CreateAndExerciseCommand_For_should_create_command_from_template()
    {
        // Arrange
        var template = new TestTemplate(new Party("Bob"), 200);
        var choice = new ChoiceName("Split");
        var arg = new DamlInt64(50);

        // Act
        var command = CreateAndExerciseCommand.For(template, choice, arg);

        // Assert
        command.CommandType.Should().Be("CreateAndExercise");
        command.TemplateId.Should().Be(TestTemplate.TemplateId);
        command.CreateArguments.GetField("owner")!.As<DamlParty>().Value.Should().Be("Bob");
        command.Choice.Should().Be(choice);
        command.ChoiceArgument.Should().Be(arg);
    }

    [Fact]
    public void CommandsSubmission_Single_should_create_submission_with_one_command()
    {
        // Arrange
        var command = new CreateCommand(
            new Identifier("pkg", "Module", "Template"),
            DamlRecord.Create());

        // Act
        var submission = CommandsSubmission.Single(command, new Party("Alice"));

        // Assert
        submission.Commands.Should().HaveCount(1);
        submission.Commands[0].Should().Be(command);
        submission.ActAs.Should().ContainSingle().Which.Should().Be(new Party("Alice"));
    }

    [Fact]
    public void CommandsSubmission_Single_should_create_submission_with_null_actAs()
    {
        // Arrange
        var command = new CreateCommand(
            new Identifier("pkg", "Module", "Template"),
            DamlRecord.Create());

        // Act
        var submission = CommandsSubmission.Single(command);

        // Assert
        submission.Commands.Should().HaveCount(1);
        submission.ActAs.Should().BeNull();
    }

    [Fact]
    public void CommandsSubmission_Multiple_should_create_submission_with_commands()
    {
        // Arrange
        var command1 = new CreateCommand(
            new Identifier("pkg", "Module", "Template1"),
            DamlRecord.Create());
        var command2 = new CreateCommand(
            new Identifier("pkg", "Module", "Template2"),
            DamlRecord.Create());

        // Act
        var submission = CommandsSubmission.Multiple(command1, command2);

        // Assert
        submission.Commands.Should().HaveCount(2);
        submission.Commands[0].Should().Be(command1);
        submission.Commands[1].Should().Be(command2);
    }

    [Fact]
    public void CommandsSubmission_should_carry_typed_ids_that_unwrap_to_their_wire_strings()
    {
        var command = new CreateCommand(
            new Identifier("pkg", "Module", "Template"),
            DamlRecord.Create());

        var submission = new CommandsSubmission(
            [command],
            new WorkflowId("wf-1"),
            new CommandId("cmd-1"));

        submission.WorkflowId!.Value.Value.Should().Be("wf-1");
        submission.CommandId!.Value.Value.Should().Be("cmd-1");
    }

    [Fact]
    public void CommandsSubmission_should_leave_optional_ids_null_when_omitted()
    {
        var command = new CreateCommand(
            new Identifier("pkg", "Module", "Template"),
            DamlRecord.Create());

        var submission = new CommandsSubmission([command]);

        submission.WorkflowId.Should().BeNull();
        submission.CommandId.Should().BeNull();
        submission.SynchronizerId.Should().BeNull();
        submission.DisclosedContracts.Should().BeNull();
    }

    [Fact]
    public void CommandsSubmission_WithWorkflowId_should_set_workflow_id()
    {
        // Arrange
        var command = new CreateCommand(
            new Identifier("pkg", "Module", "Template"),
            DamlRecord.Create());
        var submission = CommandsSubmission.Single(command);

        // Act
        var result = submission.WithWorkflowId(new WorkflowId("workflow-123"));

        // Assert
        result.WorkflowId.Should().Be(new WorkflowId("workflow-123"));
        result.Commands.Should().BeEquivalentTo(submission.Commands);
    }

    [Fact]
    public void CommandsSubmission_WithCommandId_should_set_command_id()
    {
        // Arrange
        var command = new CreateCommand(
            new Identifier("pkg", "Module", "Template"),
            DamlRecord.Create());
        var submission = CommandsSubmission.Single(command);

        // Act
        var result = submission.WithCommandId(new CommandId("cmd-456"));

        // Assert
        result.CommandId.Should().Be(new CommandId("cmd-456"));
    }

    [Fact]
    public void CommandsSubmission_WithSynchronizerId_should_set_synchronizer_id()
    {
        var command = new CreateCommand(
            new Identifier("pkg", "Module", "Template"),
            DamlRecord.Create());
        var submission = CommandsSubmission.Single(command);

        var result = submission.WithSynchronizerId(new SynchronizerId("global_sync::abc"));

        result.SynchronizerId.Should().Be(new SynchronizerId("global_sync::abc"));
        result.Commands.Should().BeEquivalentTo(submission.Commands);
    }

    [Fact]
    public void CommandsSubmission_WithActAs_should_set_parties()
    {
        // Arrange
        var command = new CreateCommand(
            new Identifier("pkg", "Module", "Template"),
            DamlRecord.Create());
        var submission = CommandsSubmission.Single(command);

        // Act
        var result = submission.WithActAs(new Party("Alice"), new Party("Bob"));

        // Assert
        result.ActAs.Should().BeEquivalentTo(new[] { new Party("Alice"), new Party("Bob") });
    }

    [Fact]
    public void CommandsSubmission_WithReadAs_should_set_parties()
    {
        // Arrange
        var command = new CreateCommand(
            new Identifier("pkg", "Module", "Template"),
            DamlRecord.Create());
        var submission = CommandsSubmission.Single(command);

        // Act
        var result = submission.WithReadAs(new Party("Charlie"), new Party("Diana"));

        // Assert
        result.ReadAs.Should().BeEquivalentTo(new[] { new Party("Charlie"), new Party("Diana") });
    }

    [Fact]
    public void CommandsSubmission_WithDisclosedContracts_should_set_disclosed_contracts()
    {
        // Arrange
        var command = new CreateCommand(
            new Identifier("pkg", "Module", "Template"),
            DamlRecord.Create());
        var submission = CommandsSubmission.Single(command);
        var disclosedContract = new DisclosedContract(
            "contract-id-1",
            new Identifier("pkg", "Module", "Template"),
            "created-event-blob"u8.ToArray());

        // Act
        var result = submission.WithDisclosedContracts(disclosedContract);

        // Assert
        result.DisclosedContracts.Should().ContainSingle().Which.Should().Be(disclosedContract);
        result.Commands.Should().BeEquivalentTo(submission.Commands);
    }

    [Fact]
    public void CommandsSubmission_WithDisclosedContracts_with_no_args_should_leave_disclosed_contracts_null()
    {
        var command = new CreateCommand(
            new Identifier("pkg", "Module", "Template"),
            DamlRecord.Create());
        var submission = CommandsSubmission.Single(command);

        var result = submission.WithDisclosedContracts();

        result.DisclosedContracts.Should().BeNull();
    }

    [Fact]
    public void CommandsSubmission_WithDisclosedContracts_with_null_array_should_leave_disclosed_contracts_null()
    {
        var command = new CreateCommand(
            new Identifier("pkg", "Module", "Template"),
            DamlRecord.Create());
        var submission = CommandsSubmission.Single(command);

        var result = submission.WithDisclosedContracts(null);

        result.DisclosedContracts.Should().BeNull();
    }

    [Fact]
    public void DisclosedContract_equality_should_compare_blob_content_across_allocations()
    {
        var left = new DisclosedContract(
            "contract-id-1",
            new Identifier("pkg", "Module", "Template"),
            "created-event-blob"u8.ToArray());
        var right = new DisclosedContract(
            "contract-id-1",
            new Identifier("pkg", "Module", "Template"),
            "created-event-blob"u8.ToArray());

        left.Should().Be(right);
        (left == right).Should().BeTrue();
        left.GetHashCode().Should().Be(right.GetHashCode());
    }

    [Fact]
    public void DisclosedContract_equality_should_distinguish_different_blob_content()
    {
        var identifier = new Identifier("pkg", "Module", "Template");
        var left = new DisclosedContract(
            "contract-id-1", identifier, "created-event-blob"u8.ToArray());
        var right = new DisclosedContract(
            "contract-id-1", identifier, "other-event-blob"u8.ToArray());

        left.Should().NotBe(right);
        (left != right).Should().BeTrue();
    }

    [Fact]
    public void CommandsSubmission_WithSubmitter_should_preserve_disclosed_contracts()
    {
        // Arrange
        var command = new CreateCommand(
            new Identifier("pkg", "Module", "Template"),
            DamlRecord.Create());
        var disclosedContract = new DisclosedContract(
            "contract-id-1",
            new Identifier("pkg", "Module", "Template"),
            "created-event-blob"u8.ToArray());
        var submission = CommandsSubmission.Single(command)
            .WithDisclosedContracts(disclosedContract);

        // Act
        var result = submission.WithSubmitter(new Party("Alice"));

        // Assert
        result.DisclosedContracts.Should().ContainSingle().Which.Should().Be(disclosedContract);
    }

    [Fact]
    public void CommandsSubmission_should_chain_fluent_methods()
    {
        // Arrange
        var command = new CreateCommand(
            new Identifier("pkg", "Module", "Template"),
            DamlRecord.Create());
        var disclosedContract = new DisclosedContract(
            "contract-id-1",
            new Identifier("pkg", "Module", "Template"),
            "created-event-blob"u8.ToArray());

        // Act
        var submission = CommandsSubmission.Single(command)
            .WithWorkflowId(new WorkflowId("workflow-1"))
            .WithCommandId(new CommandId("cmd-1"))
            .WithActAs(new Party("Alice"))
            .WithReadAs(new Party("Bob"))
            .WithDisclosedContracts(disclosedContract);

        // Assert
        submission.WorkflowId.Should().Be(new WorkflowId("workflow-1"));
        submission.CommandId.Should().Be(new CommandId("cmd-1"));
        submission.ActAs.Should().ContainSingle().Which.Should().Be(new Party("Alice"));
        submission.ReadAs.Should().ContainSingle().Which.Should().Be(new Party("Bob"));
        submission.DisclosedContracts.Should().ContainSingle().Which.Should().Be(disclosedContract);
    }

    [Fact]
    public void Choice_should_store_metadata()
    {
        // Arrange & Act
        var choice = new Choice<TestTemplate, DamlUnit, DamlUnit>
        {
            Name = new ChoiceName("Archive"),
            Consuming = true,
            ArgumentEncoder = _ => DamlUnit.Instance,
            ResultDecoder = _ => DamlUnit.Instance
        };

        // Assert
        choice.Name.Should().Be(new ChoiceName("Archive"));
        choice.Name.Value.Should().Be("Archive");
        choice.Consuming.Should().BeTrue();
    }

    [Fact]
    public void Choice_ArgumentEncoder_should_encode_argument()
    {
        // Arrange
        var choice = new Choice<TestTemplate, DamlInt64, string>
        {
            Name = new ChoiceName("GetValue"),
            Consuming = false,
            ArgumentEncoder = arg => arg,
            ResultDecoder = val => val.As<DamlText>().Value
        };

        // Act
        var encoded = choice.ArgumentEncoder(new DamlInt64(42));

        // Assert
        encoded.As<DamlInt64>().Value.Should().Be(42);
    }

    [Fact]
    public void Choice_ResultDecoder_should_decode_result()
    {
        // Arrange
        var choice = new Choice<TestTemplate, DamlUnit, long>
        {
            Name = new ChoiceName("GetCount"),
            Consuming = false,
            ArgumentEncoder = _ => DamlUnit.Instance,
            ResultDecoder = val => val.As<DamlInt64>().Value
        };

        // Act
        var decoded = choice.ResultDecoder(new DamlInt64(123));

        // Assert
        decoded.Should().Be(123);
    }
}

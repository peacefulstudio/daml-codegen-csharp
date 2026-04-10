using Daml.Runtime.Contracts;
using Daml.Runtime.Data;

namespace Daml.Runtime.Commands;

/// <summary>
/// Base interface for all ledger commands.
/// </summary>
public interface ICommand
{
    /// <summary>
    /// Gets the command type name for debugging/logging.
    /// </summary>
    string CommandType { get; }
}

/// <summary>
/// Command to create a new contract.
/// </summary>
/// <param name="TemplateId">The template identifier.</param>
/// <param name="CreateArguments">The template arguments.</param>
public sealed record CreateCommand(
    Identifier TemplateId,
    DamlRecord CreateArguments) : ICommand
{
    public string CommandType => "Create";

    /// <summary>
    /// Creates a CreateCommand for the specified template.
    /// </summary>
    public static CreateCommand For<T>(T template) where T : ITemplate =>
        new(T.TemplateId, template.ToRecord());
}

/// <summary>
/// Command to exercise a choice on a contract.
/// </summary>
/// <param name="TemplateId">The template identifier.</param>
/// <param name="ContractId">The contract ID to exercise on.</param>
/// <param name="Choice">The choice name.</param>
/// <param name="ChoiceArgument">The choice arguments.</param>
public sealed record ExerciseCommand(
    Identifier TemplateId,
    string ContractId,
    string Choice,
    DamlValue ChoiceArgument) : ICommand
{
    public string CommandType => "Exercise";

    /// <summary>
    /// Creates an ExerciseCommand for a specific contract and choice.
    /// </summary>
    public static ExerciseCommand For<T>(
        ContractId<T> contractId,
        string choice,
        DamlValue argument) where T : ITemplate =>
        new(T.TemplateId, contractId.Value, choice, argument);
}

/// <summary>
/// Command to exercise a choice on a contract identified by its key.
/// </summary>
/// <param name="TemplateId">The template identifier.</param>
/// <param name="ContractKey">The contract key.</param>
/// <param name="Choice">The choice name.</param>
/// <param name="ChoiceArgument">The choice arguments.</param>
public sealed record ExerciseByKeyCommand(
    Identifier TemplateId,
    DamlValue ContractKey,
    string Choice,
    DamlValue ChoiceArgument) : ICommand
{
    public string CommandType => "ExerciseByKey";
}

/// <summary>
/// Command to create a contract and immediately exercise a choice on it.
/// </summary>
/// <param name="TemplateId">The template identifier.</param>
/// <param name="CreateArguments">The template arguments.</param>
/// <param name="Choice">The choice name.</param>
/// <param name="ChoiceArgument">The choice arguments.</param>
public sealed record CreateAndExerciseCommand(
    Identifier TemplateId,
    DamlRecord CreateArguments,
    string Choice,
    DamlValue ChoiceArgument) : ICommand
{
    public string CommandType => "CreateAndExercise";

    /// <summary>
    /// Creates a CreateAndExerciseCommand for the specified template.
    /// </summary>
    public static CreateAndExerciseCommand For<T>(
        T template,
        string choice,
        DamlValue argument) where T : ITemplate =>
        new(T.TemplateId, template.ToRecord(), choice, argument);
}

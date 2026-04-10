using Daml.Runtime.Contracts;
using Daml.Runtime.Data;

namespace Daml.Runtime.Commands;

/// <summary>
/// Provides a fluent interface for building exercise commands.
/// </summary>
/// <typeparam name="T">The template type.</typeparam>
public interface IExercises<T> where T : ITemplate
{
    /// <summary>
    /// Gets the contract ID for this exercisable.
    /// </summary>
    ContractId<T> ContractId { get; }

    /// <summary>
    /// Exercises the Archive choice (available on all templates).
    /// </summary>
    ExerciseCommand ExerciseArchive() =>
        ExerciseCommand.For(ContractId, "Archive", DamlUnit.Instance);
}

/// <summary>
/// Provides a fluent interface for building create-and-exercise commands.
/// </summary>
/// <typeparam name="T">The template type.</typeparam>
public interface ICreateAnd<T> where T : ITemplate
{
    /// <summary>
    /// Gets the template instance to create.
    /// </summary>
    T Template { get; }

    /// <summary>
    /// Creates the contract and exercises the Archive choice.
    /// </summary>
    CreateAndExerciseCommand CreateAndExerciseArchive() =>
        CreateAndExerciseCommand.For(Template, "Archive", DamlUnit.Instance);
}

/// <summary>
/// Choice metadata for generated choice types.
/// </summary>
/// <typeparam name="TTemplate">The template type.</typeparam>
/// <typeparam name="TArg">The choice argument type.</typeparam>
/// <typeparam name="TResult">The choice result type.</typeparam>
public sealed record Choice<TTemplate, TArg, TResult>
    where TTemplate : ITemplate
{
    /// <summary>
    /// Gets the choice name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets whether this choice is consuming.
    /// </summary>
    public required bool Consuming { get; init; }

    /// <summary>
    /// Gets the function to convert the argument to a DamlValue.
    /// </summary>
    public required Func<TArg, DamlValue> ArgumentEncoder { get; init; }

    /// <summary>
    /// Gets the function to decode the result from a DamlValue.
    /// </summary>
    public required Func<DamlValue, TResult> ResultDecoder { get; init; }
}

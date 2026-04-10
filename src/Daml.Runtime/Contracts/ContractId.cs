using Daml.Runtime.Data;

namespace Daml.Runtime.Contracts;

/// <summary>
/// Represents a contract ID that references a contract of type T on the ledger.
/// </summary>
/// <typeparam name="T">The template type this contract ID refers to.</typeparam>
public record ContractId<T>(string Value) where T : ITemplate
{
    public static implicit operator string(ContractId<T> id) => id.Value;
    public static explicit operator ContractId<T>(string value) => new(value);

    public override string ToString() => Value;

    /// <summary>
    /// Converts to a DamlValue for serialization.
    /// </summary>
    public DamlContractId ToDamlValue() => new(Value, T.TemplateId);
}

/// <summary>
/// Represents a contract ID as a Daml value.
/// </summary>
public sealed record DamlContractId(string Value, Identifier? TemplateId = null) : DamlValue
{
    public ContractId<T> ToTyped<T>() where T : ITemplate => new(Value);

    public override string ToString() => Value;
}

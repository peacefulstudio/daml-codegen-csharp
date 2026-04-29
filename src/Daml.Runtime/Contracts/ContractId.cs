using Daml.Runtime.Data;

namespace Daml.Runtime.Contracts;

/// <summary>
/// Represents a contract ID that references a contract of type T on the ledger.
/// </summary>
/// <typeparam name="T">
/// The Daml template, interface, or interface placeholder this contract ID refers to.
/// Daml allows <c>ContractId I</c> where <c>I</c> is an interface (e.g.
/// <c>ContractId Holding</c> across the Splice token standard, or
/// <c>ContractId AnyContract</c> as a fully-erased marker). The codegen emits a
/// placeholder record for each such interface that implements <see cref="ITemplate"/>
/// with throwing static members — that lets <c>ContractId&lt;I&gt;</c> compile and flow
/// through the type system, while loudly failing if anyone accesses <c>I.TemplateId</c>
/// without first coercing to a real template type.
/// </typeparam>
public record ContractId<T>(string Value) where T : ITemplate
{
    public static implicit operator string(ContractId<T> id) => id.Value;
    public static explicit operator ContractId<T>(string value) => new(value);

    public override string ToString() => Value;

    /// <summary>
    /// Converts to a DamlContractId for serialization, including the static
    /// <see cref="ITemplate.TemplateId"/> of <typeparamref name="T"/>. For
    /// interface-placeholder T values the static accessor throws — see
    /// <see cref="ContractId{T}"/> for why.
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

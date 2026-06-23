// Copyright (c) 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Data;

namespace Daml.Runtime.Contracts;

/// <summary>
/// Non-generic, erased contract id: a validated ledger contract-id string with no
/// static template witness. The only concrete subtype is <see cref="ContractId{T}"/>;
/// the base is abstract on purpose, so a typeless contract id can never be fabricated.
/// </summary>
/// <remarks>
/// Construction is guarded — <see cref="ArgumentException.ThrowIfNullOrWhiteSpace"/>
/// runs in the protected constructor, so every <see cref="ContractId{T}"/> carries a
/// non-empty value. The value still projects onto the Ledger API <c>contract_id</c>
/// string via <see cref="Value"/>.
/// </remarks>
public abstract record ContractId
{
    /// <summary>The verbatim ledger contract-id string (non-null, non-whitespace).</summary>
    public string Value { get; }

    /// <summary>Constructs a contract id from a non-empty string.</summary>
    /// <param name="value">The ledger contract-id string; stored verbatim.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="value"/> is null, empty, or whitespace.</exception>
    protected ContractId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, nameof(value));
        Value = value;
    }

    /// <summary>Returns the verbatim contract-id string.</summary>
    public sealed override string ToString() => Value;
}

/// <summary>
/// Represents a contract ID that references a contract of type T on the ledger.
/// </summary>
/// <typeparam name="T">
/// The Daml template or interface this contract ID refers to. The constraint is the
/// shared marker <see cref="IDamlType"/>, satisfied by both <see cref="ITemplate"/>
/// (concrete templates) and <see cref="IDamlInterface"/> (codegen-emitted interface
/// markers). Daml allows <c>ContractId I</c> where <c>I</c> is an interface — e.g.
/// <c>ContractId Holding</c> across the Splice token standard — and that flows here as
/// <c>ContractId&lt;IHolding&gt;</c>.
/// </typeparam>
public record ContractId<T> : ContractId where T : IDamlType
{
    /// <summary>Constructs a typed contract id from a non-empty string.</summary>
    /// <param name="value">The ledger contract-id string; stored verbatim.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="value"/> is null, empty, or whitespace.</exception>
    public ContractId(string value) : base(value)
    {
    }

    /// <summary>Extracts the contract-id string; explicit so a contract id is never silently used as text.</summary>
    public static explicit operator string(ContractId<T> id) => id.Value;

    /// <summary>Parses and validates a contract-id string; explicit so arbitrary strings never silently become contract ids.</summary>
    public static explicit operator ContractId<T>(string value) => new(value);

    /// <summary>
    /// Converts to a <see cref="DamlContractId"/> for serialization, including the
    /// static type identifier of <typeparamref name="T"/>:
    /// <see cref="ITemplate.TemplateId"/> for templates, or
    /// <see cref="IDamlInterface.InterfaceId"/> for interface markers.
    /// </summary>
    /// <remarks>
    /// Resolved via reflection because instance-method generic constraints cannot
    /// be specialised by sub-interface in C#. Errors from the underlying static
    /// getter (e.g. an interface placeholder that throws on <c>TemplateId</c>
    /// access) propagate to the caller — that is the intended failure mode.
    /// </remarks>
    public DamlContractId ToDamlValue() => new(Value, ContractIdMetadata.Resolve<T>());
}

/// <summary>
/// Represents a contract ID as a Daml value.
/// </summary>
public sealed record DamlContractId(string Value, Identifier? TemplateId = null) : DamlValue
{
    /// <summary>
    /// Produces a validated <see cref="ContractId{T}"/> from this raw wire carrier.
    /// <see cref="DamlContractId"/> itself carries the contract-id string unvalidated
    /// (by design, the erased/wire contract id stays an unvalidated string; validation
    /// happens at the typed <see cref="ContractId{T}"/> boundary); this
    /// method — together with the typed <see cref="ContractId{T}"/> constructors and
    /// casts — is the validation boundary for raw wire values.
    /// </summary>
    /// <typeparam name="T">The Daml template or interface witness for the typed id.</typeparam>
    /// <exception cref="System.ArgumentException">
    /// Thrown when the carried <see cref="Value"/> is null, empty, or whitespace.
    /// </exception>
    public ContractId<T> ToTyped<T>() where T : IDamlType => new(Value);

    /// <summary>Returns the carried (unvalidated) contract-id string.</summary>
    public override string ToString() => Value;
}

/// <summary>
/// Coercion helpers between concrete-template and interface-typed contract ids.
/// </summary>
/// <remarks>
/// Mirrors Daml's <c>toInterfaceContractId @I cid</c> at the C# type level. The
/// underlying ledger contract id string is unchanged — only the static type
/// witness changes, so the result can be used with helpers constrained on
/// <see cref="IDamlInterface"/> (e.g. interface choice exercisers, the
/// interface-view dispatch in <c>tx.Single&lt;I&gt;()</c>). The
/// <see cref="IImplements{TInterface}"/> constraint at the call site enforces
/// that the concrete template actually implements the interface — codegen
/// emits the marker on every <c>template T implements I where ...</c>.
/// </remarks>
public static class ContractIdInterfaceCoercion
{
    /// <summary>
    /// Reinterprets a template-typed contract id as an interface-typed one.
    /// The runtime contract id string is preserved.
    /// </summary>
    /// <typeparam name="TConcrete">The concrete template type.</typeparam>
    /// <typeparam name="TInterface">The interface marker type.</typeparam>
    public static ContractId<TInterface> ToInterfaceContractId<TConcrete, TInterface>(
        this ContractId<TConcrete> id)
        where TConcrete : ITemplate, IImplements<TInterface>
        where TInterface : IDamlInterface
    {
        ArgumentNullException.ThrowIfNull(id);
        return new ContractId<TInterface>(id.Value);
    }
}

/// <summary>
/// Discriminates whether a Daml type was resolved as a concrete template or as an
/// interface marker, selecting which identifier kind the match carries.
/// </summary>
internal enum DamlTypeMatch
{
    Template,
    Interface,
}

internal readonly record struct ResolvedDamlType(Identifier Identifier, DamlTypeMatch Match);

internal static class ContractIdMetadata
{
    public static Identifier Resolve<T>() where T : IDamlType => ResolveMatch<T>().Identifier;

    /// <summary>
    /// Resolves the static type identifier carried by a closed
    /// <see cref="ContractId{T}"/>: <see cref="ITemplate.TemplateId"/> for templates,
    /// <see cref="IDamlInterface.InterfaceId"/> for interface markers.
    /// </summary>
    public static ResolvedDamlType ResolveMatch<T>() where T : IDamlType
    {
        if (typeof(ITemplate).IsAssignableFrom(typeof(T)))
        {
            return new ResolvedDamlType(
                ResolveStaticIdentifier(typeof(T), nameof(ITemplate.TemplateId)),
                DamlTypeMatch.Template);
        }

        if (typeof(IDamlInterface).IsAssignableFrom(typeof(T)))
        {
            return new ResolvedDamlType(
                ResolveStaticIdentifier(typeof(T), nameof(IDamlInterface.InterfaceId)),
                DamlTypeMatch.Interface);
        }

        throw new InvalidOperationException(
            $"Type '{typeof(T).FullName}' implements IDamlType but exposes neither "
            + "ITemplate.TemplateId nor IDamlInterface.InterfaceId statically.");
    }

    private const System.Reflection.BindingFlags StaticIdentifierBindingFlags =
        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic
        | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.FlattenHierarchy;

    private static Identifier ResolveStaticIdentifier(Type type, string propertyName) =>
        ResolveDirectlyDeclaredIdentifier(type, propertyName)
            ?? ResolveExplicitInterfaceIdentifier(type, propertyName)
            ?? throw new InvalidOperationException(
                $"Type '{type.FullName}' exposes no static Identifier {propertyName} property.");

    private static Identifier? ResolveDirectlyDeclaredIdentifier(Type type, string propertyName)
    {
        var prop = type.GetProperty(propertyName, StaticIdentifierBindingFlags);
        return prop is not null && IsConcreteIdentifierGetter(prop)
            ? (Identifier)InvokeStaticGetter(prop)!
            : null;
    }

    private static Identifier? ResolveExplicitInterfaceIdentifier(Type type, string propertyName)
    {
        foreach (var candidateType in EnumerateTypeAndInterfaces(type))
        {
            foreach (var prop in candidateType.GetProperties(StaticIdentifierBindingFlags))
            {
                if (IsConcreteIdentifierGetter(prop) && TrailingSegment(prop.Name) == propertyName)
                {
                    return (Identifier)InvokeStaticGetter(prop)!;
                }
            }
        }
        return null;
    }

    private static bool IsConcreteIdentifierGetter(System.Reflection.PropertyInfo prop) =>
        prop.PropertyType == typeof(Identifier) && prop.GetMethod is { IsAbstract: false };

    private static string TrailingSegment(string memberName)
    {
        var lastDot = memberName.LastIndexOf('.');
        return lastDot >= 0 ? memberName[(lastDot + 1)..] : memberName;
    }

    private static IEnumerable<Type> EnumerateTypeAndInterfaces(Type type)
    {
        yield return type;
        foreach (var iface in type.GetInterfaces())
        {
            yield return iface;
        }
    }

    private static object? InvokeStaticGetter(System.Reflection.PropertyInfo prop)
    {
        try
        {
            return prop.GetValue(null);
        }
        catch (System.Reflection.TargetInvocationException tie) when (tie.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(tie.InnerException).Throw();
            throw;
        }
    }
}

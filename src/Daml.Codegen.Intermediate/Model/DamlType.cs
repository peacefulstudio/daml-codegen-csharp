// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Daml.Codegen.Intermediate.Model;

/// <summary>
/// Represents a Daml type.
/// </summary>
public abstract record DamlType
{
    /// <summary>
    /// Gets whether this type is optional.
    /// </summary>
    public virtual bool IsOptional => false;
}

/// <summary>
/// A primitive Daml type.
/// </summary>
public sealed record DamlPrimitiveType(DamlPrimitive Primitive) : DamlType;

/// <summary>
/// Enumeration of Daml primitive types.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Naming",
    "CA1720:Identifier contains type name",
    Justification = "Members name Daml-LF builtin types; Int64 is the spelling the Daml-LF specification and the intermediate protobuf use.")]
public enum DamlPrimitive
{
    /// <summary>Daml <c>()</c> — the empty value.</summary>
    Unit,

    /// <summary>Daml <c>Bool</c>.</summary>
    Bool,

    /// <summary>Daml <c>Int</c> — 64-bit signed integer.</summary>
    Int64,

    /// <summary>Daml <c>Numeric n</c> — fixed-scale decimal.</summary>
    Numeric,

    /// <summary>Daml <c>Text</c> — a string.</summary>
    Text,

    /// <summary>Daml <c>Date</c> — a calendar date without time.</summary>
    Date,

    /// <summary>Daml <c>Time</c> — a timestamp with microsecond precision.</summary>
    Timestamp,

    /// <summary>Daml <c>Party</c> — a ledger party identifier.</summary>
    Party,

    /// <summary>Daml <c>ContractId a</c> — takes the template type as an argument.</summary>
    ContractId,

    /// <summary>Daml <c>[a]</c> — takes the element type as an argument.</summary>
    List,

    /// <summary>Daml <c>Optional a</c> — takes the element type as an argument.</summary>
    Optional,

    /// <summary>Daml <c>TextMap a</c> — string-keyed map, takes the value type as an argument.</summary>
    TextMap,

    /// <summary>Daml <c>GenMap k v</c> — takes the key and value types as arguments.</summary>
    GenMap
}

/// <summary>
/// A reference to a user-defined type.
/// </summary>
public sealed record DamlTypeRef(string PackageId, string Module, string Name) : DamlType;

/// <summary>
/// A type application (generic type with arguments).
/// </summary>
public sealed record DamlTypeApp(DamlType Base, IReadOnlyList<DamlType> Arguments) : DamlType
{
    /// <summary>True when this application is <c>Optional a</c>, i.e. the base is the Optional primitive.</summary>
    public override bool IsOptional =>
        Base is DamlPrimitiveType { Primitive: DamlPrimitive.Optional };

    /// <summary>
    /// Compares by value, including <see cref="Arguments"/> element by element. The
    /// compiler-synthesized record equality would compare that list by reference, which
    /// makes two independently-built but structurally identical type trees unequal.
    /// </summary>
    public bool Equals(DamlTypeApp? other) =>
        other is not null && Base.Equals(other.Base) && Arguments.SequenceEqual(other.Arguments);

    /// <inheritdoc cref="Equals(DamlTypeApp?)"/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Base);
        foreach (var argument in Arguments)
        {
            hash.Add(argument);
        }
        return hash.ToHashCode();
    }
}

/// <summary>
/// A type variable.
/// </summary>
public sealed record DamlTypeVar(string Name) : DamlType;

/// <summary>
/// A Daml <c>Optional a</c> in a position C# nullable syntax cannot carry, emitted as the
/// runtime wrapper rather than as <c>t?</c>. Produced only by the representation pre-pass,
/// which is the sole owner of the rule deciding which positions those are.
/// </summary>
/// <param name="Argument">The type the optional carries.</param>
/// <param name="Encoding">The wire encoding this position requires.</param>
public sealed record DamlWrappedOptional(DamlType Argument, OptionalEncoding Encoding) : DamlType
{
    /// <inheritdoc />
    public override bool IsOptional => true;
}

/// <summary>
/// The wire encoding a Daml <c>Optional</c> carries, which depends on its position rather
/// than on its C# representation.
/// </summary>
public enum OptionalEncoding
{
    /// <summary>JSON <c>null</c> when absent, the bare value when present.</summary>
    Flat,

    /// <summary>
    /// JSON <c>[]</c> when absent, <c>[v]</c> when present — the form a participant requires
    /// at every level of an Optional chain nested two or more levels deep.
    /// </summary>
    NestedChain
}

// Copyright (c) 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Daml.Codegen.CSharp.Model;

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
}

/// <summary>
/// A type variable.
/// </summary>
public sealed record DamlTypeVar(string Name) : DamlType;

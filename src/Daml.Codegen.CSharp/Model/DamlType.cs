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
    Unit,
    Bool,
    Int64,
    Numeric,
    Text,
    Date,
    Timestamp,
    Party,
    ContractId,
    List,
    Optional,
    TextMap,
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
    public override bool IsOptional =>
        Base is DamlPrimitiveType { Primitive: DamlPrimitive.Optional };
}

/// <summary>
/// A type variable.
/// </summary>
public sealed record DamlTypeVar(string Name) : DamlType;

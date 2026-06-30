// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Daml.Codegen.CSharp.Model;

/// <summary>
/// Represents a user-defined Daml data type.
/// </summary>
public sealed class DamlDataType
{
    /// <summary>
    /// Gets the type name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the type parameters.
    /// </summary>
    public IReadOnlyList<string> TypeParams { get; init; } = [];

    /// <summary>
    /// Gets whether this is serializable.
    /// </summary>
    public bool Serializable { get; init; } = true;

    /// <summary>
    /// Gets the definition of this data type.
    /// </summary>
    public required DamlDataTypeDefinition Definition { get; init; }
}

/// <summary>
/// Base class for data type definitions.
/// </summary>
public abstract record DamlDataTypeDefinition;

/// <summary>
/// A record (product) type definition.
/// </summary>
public sealed record DamlRecordDefinition(IReadOnlyList<DamlFieldDefinition> Fields) : DamlDataTypeDefinition;

/// <summary>
/// A variant (sum) type definition.
/// </summary>
public sealed record DamlVariantDefinition(IReadOnlyList<DamlVariantConstructor> Constructors) : DamlDataTypeDefinition;

/// <summary>
/// An enum type definition.
/// </summary>
public sealed record DamlEnumDefinition(IReadOnlyList<string> Constructors) : DamlDataTypeDefinition;

/// <summary>
/// A variant constructor.
/// </summary>
public sealed record DamlVariantConstructor(string Name, DamlType? ArgumentType);

/// <summary>
/// Represents a field in a record.
/// </summary>
public sealed record DamlFieldDefinition(string Name, DamlType Type);

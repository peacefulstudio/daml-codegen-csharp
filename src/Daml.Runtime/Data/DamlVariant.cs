// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Daml.Runtime.Data;

/// <summary>
/// Represents a Daml Variant (sum type) value.
/// </summary>
/// <param name="VariantId">Optional type identifier.</param>
/// <param name="Constructor">The name of the variant constructor.</param>
/// <param name="Value">The value associated with this variant constructor.</param>
public sealed record DamlVariant(
    Identifier? VariantId,
    string Constructor,
    DamlValue Value) : DamlValue
{
    /// <summary>Builds a variant without a type identifier, e.g. when the type is implied by context.</summary>
    public static DamlVariant Create(string constructor, DamlValue value) =>
        new(null, constructor, value);

    /// <summary>Builds a variant tagged with its Daml type identifier.</summary>
    public static DamlVariant Create(Identifier variantId, string constructor, DamlValue value) =>
        new(variantId, constructor, value);

    /// <summary>True when this variant was built with the given constructor name.</summary>
    public bool Is(string constructor) =>
        Constructor == constructor;

    /// <summary>The carried value downcast to <typeparamref name="T"/>; throws <see cref="InvalidCastException"/> on mismatch.</summary>
    public T GetValue<T>() where T : DamlValue =>
        (T)Value;
}

/// <summary>
/// Represents a Daml Enum value.
/// </summary>
/// <param name="EnumId">Optional type identifier.</param>
/// <param name="Constructor">The enum constructor name.</param>
public sealed record DamlEnum(Identifier? EnumId, string Constructor) : DamlValue
{
    /// <summary>Builds an enum value without a type identifier, e.g. when the type is implied by context.</summary>
    public static DamlEnum Create(string constructor) => new(null, constructor);

    /// <summary>Builds an enum value tagged with its Daml type identifier.</summary>
    public static DamlEnum Create(Identifier enumId, string constructor) => new(enumId, constructor);

    /// <summary>True when this enum value is the given constructor.</summary>
    public bool Is(string constructor) => Constructor == constructor;
}

// Copyright (c) 2026 Peaceful Studio OÜ
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
    public static DamlVariant Create(string constructor, DamlValue value) =>
        new(null, constructor, value);

    public static DamlVariant Create(Identifier variantId, string constructor, DamlValue value) =>
        new(variantId, constructor, value);

    public bool Is(string constructor) =>
        Constructor == constructor;

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
    public static DamlEnum Create(string constructor) => new(null, constructor);
    public static DamlEnum Create(Identifier enumId, string constructor) => new(enumId, constructor);

    public bool Is(string constructor) => Constructor == constructor;
}

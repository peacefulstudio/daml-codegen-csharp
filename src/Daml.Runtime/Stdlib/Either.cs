// Copyright (c) 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Data;

namespace Daml.Runtime.Stdlib;

/// <summary>
/// Runtime backing for the Daml stdlib type <c>DA.Types.Either</c>.
/// </summary>
/// <typeparam name="TL">Type carried by the <see cref="Left"/> constructor.</typeparam>
/// <typeparam name="TR">Type carried by the <see cref="Right"/> constructor.</typeparam>
public abstract record Either<TL, TR>
{
    private Either()
    {
    }

    /// <summary>
    /// The <c>Left</c> constructor of <c>DA.Types.Either</c>.
    /// </summary>
    /// <param name="Value">The carried left value.</param>
    public sealed record Left(TL Value) : Either<TL, TR>;

    /// <summary>
    /// The <c>Right</c> constructor of <c>DA.Types.Either</c>.
    /// </summary>
    /// <param name="Value">The carried right value.</param>
    public sealed record Right(TR Value) : Either<TL, TR>;

    /// <summary>
    /// Converts this value to its <see cref="DamlValue"/> wire representation.
    /// </summary>
    /// <param name="convertLeft">Converter for the left value.</param>
    /// <param name="convertRight">Converter for the right value.</param>
    /// <returns>A <see cref="DamlVariant"/> wire value.</returns>
    public DamlValue ToValue(Func<TL, DamlValue> convertLeft, Func<TR, DamlValue> convertRight)
    {
        ArgumentNullException.ThrowIfNull(convertLeft);
        ArgumentNullException.ThrowIfNull(convertRight);
        return this switch
        {
            Left left => DamlVariant.Create("Left", convertLeft(left.Value)),
            Right right => DamlVariant.Create("Right", convertRight(right.Value)),
            _ => throw new InvalidOperationException($"Unknown Either constructor: {GetType().Name}"),
        };
    }

    /// <summary>
    /// Reconstructs an <see cref="Either{TL,TR}"/> from its <see cref="DamlValue"/> wire representation.
    /// </summary>
    /// <param name="value">The wire value, expected to be a <see cref="DamlVariant"/>.</param>
    /// <param name="convertLeft">Converter for the left value.</param>
    /// <param name="convertRight">Converter for the right value.</param>
    /// <returns>The reconstructed value.</returns>
    public static Either<TL, TR> FromValue(
        DamlValue value,
        Func<DamlValue, TL> convertLeft,
        Func<DamlValue, TR> convertRight)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(convertLeft);
        ArgumentNullException.ThrowIfNull(convertRight);
        var variant = value.As<DamlVariant>();
        return variant.Constructor switch
        {
            "Left" => new Left(convertLeft(variant.Value)),
            "Right" => new Right(convertRight(variant.Value)),
            _ => throw new InvalidOperationException($"Unknown Either constructor: {variant.Constructor}"),
        };
    }
}

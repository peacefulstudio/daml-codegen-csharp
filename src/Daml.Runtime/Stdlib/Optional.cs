// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using Daml.Runtime.Data;

namespace Daml.Runtime.Stdlib;

/// <summary>
/// A Daml <c>Optional a</c> in the positions where C# nullable syntax cannot carry it —
/// over a type parameter, as a type argument to a generated generic, as a
/// <c>GenMap</c> key, or nested inside another Optional. Elsewhere an Optional is emitted
/// as <c>t?</c>.
/// </summary>
/// <typeparam name="T">The carried type.</typeparam>
[SuppressMessage(
    "Naming",
    "CA1716:Identifiers should not match keywords",
    Justification = "Optional is the Daml stdlib type this represents, and the name is the user-facing half of the runtime's wire/user split alongside DamlOptional; the conflict is with a Visual Basic keyword, and no consumer of this package targets Visual Basic.")]
[SuppressMessage(
    "Design",
    "CA1000:Do not declare static members on generic types",
    Justification = "The static factory is the wire-decoding entry point for this Daml stdlib shape; generated code calls it as Optional<...>.FromValue, mirroring the Daml constructor it decodes.")]
public abstract record Optional<T>
    where T : notnull
{
    private protected Optional() { }

    /// <summary>True when this optional is <see cref="Some"/>; false when it is <see cref="None"/>.</summary>
    public abstract bool HasValue { get; }

    /// <summary>
    /// Applies the handler for this optional's arm and returns its result.
    /// </summary>
    /// <remarks>
    /// Every arm is a parameter, so adding one changes this signature and a consumer that
    /// projects through it stops compiling instead of falling through a default branch. A
    /// <c>switch</c> cannot offer that: C# treats a class hierarchy as open, so a switch
    /// expression covering both arms is still non-exhaustive (CS8509) and needs a discard
    /// arm that would silently swallow a new one.
    /// </remarks>
    /// <typeparam name="TResult">The projection's result type.</typeparam>
    /// <param name="some">Handler for <see cref="Some"/>, receiving its carried value.</param>
    /// <param name="none">Handler for <see cref="None"/>.</param>
    /// <returns>The selected handler's result.</returns>
    /// <exception cref="ArgumentNullException">Either handler is <see langword="null"/>.</exception>
    public abstract TResult Match<TResult>(Func<T, TResult> some, Func<TResult> none);

    /// <summary>The carried value, or <see langword="default"/> when <see cref="None"/>.</summary>
    /// <remarks>
    /// A single <c>ToNullable()</c> returning <c>T?</c> is not expressible: it would need
    /// <c>where T : class</c> and <c>where T : struct</c> overloads, and C# forbids overloads
    /// differing only by type-parameter constraints.
    /// </remarks>
    /// <returns>The carried value, or <see langword="default"/>.</returns>
    public abstract T? GetValueOrDefault();

    /// <summary>The carried value.</summary>
    /// <returns>The carried value.</returns>
    /// <exception cref="InvalidOperationException">This optional is <see cref="None"/>.</exception>
    public abstract T GetValueOrThrow();

    /// <summary>Attempts to read the carried value.</summary>
    /// <param name="value">The carried value when this optional is <see cref="Some"/>.</param>
    /// <returns><see langword="true"/> when this optional is <see cref="Some"/>.</returns>
    public abstract bool TryGetValue([MaybeNullWhen(false)] out T value);

    /// <summary>
    /// Converts this value to its <see cref="DamlValue"/> wire representation, in the flat
    /// <c>null</c>-or-value encoding a Daml <c>Optional a</c> carries wherever <c>a</c> is
    /// not itself an Optional.
    /// </summary>
    /// <param name="convert">Converter for the carried value.</param>
    /// <returns>The wire value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="convert"/> is <see langword="null"/>.</exception>
    public DamlValue ToValue(Func<T, DamlValue> convert)
    {
        ArgumentNullException.ThrowIfNull(convert);
        return Match<DamlValue>(
            value => DamlOptional.Some(convert(value)),
            () => DamlOptional.None);
    }

    /// <summary>
    /// Reconstructs an <see cref="Optional{T}"/> from its flat <see cref="DamlValue"/> wire
    /// representation.
    /// </summary>
    /// <param name="value">The wire value.</param>
    /// <param name="convert">Converter for the carried value.</param>
    /// <returns>The reconstructed value.</returns>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    public static Optional<T> FromValue(DamlValue value, Func<DamlValue, T> convert)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(convert);
        var optional = value.As<DamlOptional>();
        return optional.Value is null ? new None() : new Some(convert(optional.Value));
    }

    /// <summary>
    /// Converts this value to its <see cref="DamlValue"/> wire representation in the array
    /// encoding every level of a nested Optional chain carries.
    /// </summary>
    /// <param name="convert">Converter for the carried value.</param>
    /// <returns>The wire value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="convert"/> is <see langword="null"/>.</exception>
    public DamlValue ToChainValue(Func<T, DamlValue> convert)
    {
        ArgumentNullException.ThrowIfNull(convert);
        return Match<DamlValue>(
            value => DamlOptionalChain.Some(convert(value)),
            () => DamlOptionalChain.None);
    }

    /// <summary>
    /// Reconstructs an <see cref="Optional{T}"/> from the array encoding a nested Optional
    /// chain carries.
    /// </summary>
    /// <param name="value">The wire value.</param>
    /// <param name="convert">Converter for the carried value.</param>
    /// <returns>The reconstructed value.</returns>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    public static Optional<T> FromChainValue(DamlValue value, Func<DamlValue, T> convert)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(convert);
        var chain = value.As<DamlOptionalChain>();
        return chain.Value is null ? new None() : new Some(convert(chain.Value));
    }

    /// <summary>
    /// The present arm, mirroring Daml's <c>Some</c>.
    /// </summary>
    /// <param name="Value">The carried value.</param>
    public sealed record Some(T Value) : Optional<T>
    {
        /// <inheritdoc />
        public override bool HasValue => true;

        /// <inheritdoc />
        public override TResult Match<TResult>(Func<T, TResult> some, Func<TResult> none)
        {
            ArgumentNullException.ThrowIfNull(some);
            ArgumentNullException.ThrowIfNull(none);
            return some(Value);
        }

        /// <inheritdoc />
        public override T? GetValueOrDefault() => Value;

        /// <inheritdoc />
        public override T GetValueOrThrow() => Value;

        /// <inheritdoc />
        public override bool TryGetValue([MaybeNullWhen(false)] out T value)
        {
            value = Value;
            return true;
        }
    }

    /// <summary>
    /// The absent arm, mirroring Daml's <c>None</c>.
    /// </summary>
    public sealed record None : Optional<T>
    {
        /// <inheritdoc />
        public override bool HasValue => false;

        /// <inheritdoc />
        public override TResult Match<TResult>(Func<T, TResult> some, Func<TResult> none)
        {
            ArgumentNullException.ThrowIfNull(some);
            ArgumentNullException.ThrowIfNull(none);
            return none();
        }

        /// <inheritdoc />
        public override T? GetValueOrDefault() => default;

        /// <inheritdoc />
        public override T GetValueOrThrow() =>
            throw new InvalidOperationException("Optional value is None.");

        /// <inheritdoc />
        public override bool TryGetValue([MaybeNullWhen(false)] out T value)
        {
            value = default;
            return false;
        }
    }
}

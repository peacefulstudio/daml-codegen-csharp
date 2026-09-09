// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Collections.ObjectModel;

namespace Daml.Runtime.Contracts;

/// <summary>
/// Null guards and defensive copies for the collection-typed members that still take a caller's
/// <see cref="IReadOnlyList{T}"/>, <see cref="IEnumerable{T}"/> or
/// <see cref="IReadOnlyDictionary{TKey,TValue}"/>: <see cref="CaughtException.Metadata"/>,
/// <see cref="Outcomes.ExerciseOutcome{T}.DamlError.Metadata"/>, and
/// the emitter-facing command and value shapes — <c>Commands.CommandsSubmission</c>,
/// <see cref="Data.DamlRecord"/> and the <c>Daml.Runtime.Stdlib</c> collection types — that
/// generated code is compiled against. The event records' list members carry
/// <see cref="EquatableArray{T}"/> instead, which copies at construction and has no <c>null</c>
/// state, so they need nothing from here.
/// </summary>
/// <remarks>
/// A read-only interface is a view, not an immutable collection: a producer that keeps its
/// backing collection can mutate it after handing it over, and on a record whose equality and
/// hash code read the contents that would silently change an already-computed hash and make the
/// value unfindable in a set or dictionary that already holds it. Every caller here therefore
/// copies at each entry point — every constructor and <c>init</c> accessor, so <c>with</c>
/// expressions are covered too.
/// </remarks>
internal static class EventCollections
{
    internal static IReadOnlyList<T> Copy<T>(IReadOnlyList<T> values, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(values, parameterName);
        return values.Count == 0 ? Array.Empty<T>() : [.. values];
    }

    internal static IReadOnlySet<T> Copy<T>(IEnumerable<T> values, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(values, parameterName);
        return values.TryGetNonEnumeratedCount(out var count) && count == 0
            ? ReadOnlySet<T>.Empty
            : values.ToHashSet();
    }

    internal static IReadOnlyDictionary<TKey, TValue> Copy<TKey, TValue>(
        IReadOnlyDictionary<TKey, TValue> values,
        string parameterName)
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(values, parameterName);
        return values.Count == 0 ? ReadOnlyDictionary<TKey, TValue>.Empty : values.ToDictionary();
    }
}

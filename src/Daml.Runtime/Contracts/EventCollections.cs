// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Collections.ObjectModel;

namespace Daml.Runtime.Contracts;

/// <summary>
/// Null guards and defensive copies for the collection-typed members of the event records
/// in this namespace and of the <c>Daml.Runtime.Stdlib</c> value types. The <c>Copy</c>
/// overloads materialize, mirroring what
/// <see cref="Data.DamlList"/>, <see cref="Data.DamlTextMap"/> and <see cref="Data.DamlGenMap"/>
/// already do for the value shapes; <see cref="Borrow{T}"/> only checks, keeping the
/// producer's own instance.
/// </summary>
/// <remarks>
/// <see cref="IReadOnlyList{T}"/> and <see cref="IReadOnlyDictionary{TKey,TValue}"/> are
/// read-only <em>views</em>, not immutable collections: a producer that keeps its backing
/// <see cref="List{T}"/> can mutate it after handing it over. On the records whose equality
/// and hash codes read the contents, that would silently change an already-computed hash and
/// make the value unfindable in a set or dictionary that already holds it, so those copy at
/// every entry point — the primary constructor and each <c>init</c> accessor, so <c>with</c>
/// expressions are covered too. The records that keep record-synthesized equality compare
/// their collection member by reference, so no hash can be corrupted and copying would
/// instead narrow their equality to near-identity while allocating on the read path; those
/// borrow, and their doc-comments state the caller's obligation not to mutate.
/// </remarks>
internal static class EventCollections
{
    internal static IReadOnlyList<T> Borrow<T>(IReadOnlyList<T> values, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(values, parameterName);
        return values;
    }

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

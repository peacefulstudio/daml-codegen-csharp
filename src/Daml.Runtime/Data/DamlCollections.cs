// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;

namespace Daml.Runtime.Data;

/// <summary>
/// Represents a Daml Optional value.
/// </summary>
public sealed record DamlOptional(DamlValue? Value) : DamlValue
{
    /// <summary>True when this optional is Some; false when it is None.</summary>
    public bool HasValue => Value is not null;

    /// <summary>The empty optional, mirroring Daml's <c>None</c>.</summary>
    public static DamlOptional None => new(default(DamlValue));

    /// <summary>Wraps a value as a present optional, mirroring Daml's <c>Some</c>.</summary>
    public static DamlOptional Some(DamlValue value) => new(value);

    /// <summary>The carried value as <typeparamref name="T"/>, or null when None or of a different type.</summary>
    public T? GetValueOrDefault<T>() where T : DamlValue =>
        Value as T;

    /// <summary>The carried value; throws <see cref="InvalidOperationException"/> when None.</summary>
    public DamlValue GetValueOrThrow() =>
        Value ?? throw new InvalidOperationException("Optional value is None.");
}

/// <summary>
/// Represents a Daml List value.
/// </summary>
public sealed record DamlList(IReadOnlyList<DamlValue> Values) : DamlValue
{
    /// <summary>The number of elements in the list.</summary>
    public int Count => Values.Count;

    /// <summary>The element at the given zero-based position.</summary>
    public DamlValue this[int index] => Values[index];

    /// <summary>Builds a list from the given elements, preserving order.</summary>
    public static DamlList Create(params DamlValue[] values) => new(values);

    /// <summary>Builds a list from a sequence, materializing it so later mutation of the source has no effect.</summary>
    public static DamlList Create(IEnumerable<DamlValue> values) => new(values.ToList());

    /// <summary>The elements downcast to <typeparamref name="T"/>; throws on first mismatched element when enumerated.</summary>
    public IEnumerable<T> AsEnumerable<T>() where T : DamlValue =>
        Values.Cast<T>();

    /// <summary>
    /// Compares two lists element by element. The record-synthesized equality
    /// compares the backing <see cref="IReadOnlyList{T}"/> by reference — a
    /// footgun for a value type — so we override it with structural comparison.
    /// </summary>
    public bool Equals(DamlList? other) =>
        other is not null && Values.SequenceEqual(other.Values);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var value in Values)
        {
            hash.Add(value);
        }
        return hash.ToHashCode();
    }
}

/// <summary>
/// Represents a Daml TextMap value (map with string keys).
/// </summary>
public sealed record DamlTextMap(IReadOnlyDictionary<string, DamlValue> Values) : DamlValue
{
    /// <summary>The number of entries in the map.</summary>
    public int Count => Values.Count;

    /// <summary>The value stored under the given key; throws <see cref="KeyNotFoundException"/> when absent.</summary>
    public DamlValue this[string key] => Values[key];

    /// <summary>Builds a text map from key/value pairs; duplicate keys throw.</summary>
    public static DamlTextMap Create(params (string Key, DamlValue Value)[] entries) =>
        new(entries.ToDictionary(e => e.Key, e => e.Value));

    /// <summary>Looks up a key without throwing; returns false and a null value when absent.</summary>
    public bool TryGetValue(string key, [NotNullWhen(true)] out DamlValue? value) =>
        Values.TryGetValue(key, out value);

    /// <summary>
    /// Compares two maps by key-value content, independent of insertion order.
    /// The record-synthesized equality compares the backing
    /// <see cref="IReadOnlyDictionary{TKey,TValue}"/> by reference — a footgun
    /// for a value type — so we override it with structural comparison.
    /// </summary>
    public bool Equals(DamlTextMap? other)
    {
        if (other is null || Values.Count != other.Values.Count)
        {
            return false;
        }
        foreach (var (key, value) in Values)
        {
            if (!other.Values.TryGetValue(key, out var otherValue) || !value.Equals(otherValue))
            {
                return false;
            }
        }
        return true;
    }

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = 0;
        foreach (var (key, value) in Values)
        {
            hash ^= HashCode.Combine(key, value);
        }
        return hash;
    }
}

/// <summary>
/// Represents a Daml GenMap value (map with arbitrary key types).
/// </summary>
public sealed record DamlGenMap(IReadOnlyList<(DamlValue Key, DamlValue Value)> Entries) : DamlValue
{
    /// <summary>The number of entries in the map.</summary>
    public int Count => Entries.Count;

    /// <summary>
    /// Builds a generic map from key/value pairs, preserving entry order; keys that are
    /// structurally equal throw <see cref="ArgumentException"/>, mirroring
    /// <see cref="DamlTextMap.Create"/>.
    /// </summary>
    public static DamlGenMap Create(params (DamlValue Key, DamlValue Value)[] entries)
    {
        var seenKeys = new HashSet<DamlValue>();
        foreach (var (key, _) in entries)
        {
            if (!seenKeys.Add(key))
            {
                throw new ArgumentException($"GenMap has a duplicate key '{key}'.", nameof(entries));
            }
        }
        return new(entries);
    }

    /// <summary>
    /// Compares two maps entry by entry, in entry order (GenMap entries are
    /// ordered on the ledger). The record-synthesized equality compares the
    /// backing <see cref="IReadOnlyList{T}"/> by reference — a footgun for a
    /// value type — so we override it with structural comparison.
    /// </summary>
    public bool Equals(DamlGenMap? other) =>
        other is not null && Entries.SequenceEqual(other.Entries);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var entry in Entries)
        {
            hash.Add(entry);
        }
        return hash.ToHashCode();
    }
}

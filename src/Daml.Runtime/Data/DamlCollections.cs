// Copyright (c) 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Daml.Runtime.Data;

/// <summary>
/// Represents a Daml Optional value.
/// </summary>
public sealed record DamlOptional(DamlValue? Value) : DamlValue
{
    public bool HasValue => Value is not null;

    public static DamlOptional None => new(default(DamlValue));
    public static DamlOptional Some(DamlValue value) => new(value);

    public T? GetValueOrDefault<T>() where T : DamlValue =>
        Value as T;

    public DamlValue GetValueOrThrow() =>
        Value ?? throw new InvalidOperationException("Optional value is None.");
}

/// <summary>
/// Represents a Daml List value.
/// </summary>
public sealed record DamlList(IReadOnlyList<DamlValue> Values) : DamlValue
{
    public int Count => Values.Count;
    public DamlValue this[int index] => Values[index];

    public static DamlList Create(params DamlValue[] values) => new(values);
    public static DamlList Create(IEnumerable<DamlValue> values) => new(values.ToList());

    public IEnumerable<T> AsEnumerable<T>() where T : DamlValue =>
        Values.Cast<T>();
}

/// <summary>
/// Represents a Daml TextMap value (map with string keys).
/// </summary>
public sealed record DamlTextMap(IReadOnlyDictionary<string, DamlValue> Values) : DamlValue
{
    public int Count => Values.Count;
    public DamlValue this[string key] => Values[key];

    public static DamlTextMap Create(params (string Key, DamlValue Value)[] entries) =>
        new(entries.ToDictionary(e => e.Key, e => e.Value));

    public bool TryGetValue(string key, out DamlValue? value) =>
        Values.TryGetValue(key, out value);
}

/// <summary>
/// Represents a Daml GenMap value (map with arbitrary key types).
/// </summary>
public sealed record DamlGenMap(IReadOnlyList<(DamlValue Key, DamlValue Value)> Entries) : DamlValue
{
    public int Count => Entries.Count;

    public static DamlGenMap Create(params (DamlValue Key, DamlValue Value)[] entries) =>
        new(entries);
}

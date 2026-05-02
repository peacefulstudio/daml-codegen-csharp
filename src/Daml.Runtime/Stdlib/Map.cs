using Daml.Runtime.Data;

namespace Daml.Runtime.Stdlib;

/// <summary>
/// Daml stdlib type <c>DA.Map.Types.Map k v</c> / <c>DA.Internal.Map.Map k v</c> —
/// an ordered associative map.
/// </summary>
/// <remarks>
/// <para>
/// In Daml stdlib, <c>DA.Map.Types.Map k v</c> is a record wrapper over the
/// <c>GenMap</c> primitive — roughly <c>data Map k v = Map { map :: GenMap k v }</c> —
/// so the Ledger API wire shape is a record with a single field named
/// <c>map</c> carrying a <see cref="DamlGenMap"/>. This stub mirrors that
/// shape exactly. It is rarely reached: the codegen routes most uses of
/// <c>GenMap k v</c> directly to <see cref="IReadOnlyDictionary{TKey, TValue}"/>
/// backed by <see cref="DamlGenMap"/>, and only falls through to this wrapper
/// when a DAR references the type by its <c>DA.Map.Types</c> or
/// <c>DA.Internal.Map</c> module path explicitly.
/// </para>
/// <para>
/// The wrapper carries an ordered list of key/value pairs, matching the
/// order-preserving <c>GenMap</c> wire shape. No CLR <c>Dictionary</c>-backed
/// view is exposed because <c>GenMap</c> tolerates structurally-equal duplicate
/// keys that <see cref="System.Collections.Generic.Dictionary{TKey, TValue}"/>
/// would reject.
/// </para>
/// </remarks>
/// <typeparam name="TKey">Key type.</typeparam>
/// <typeparam name="TValue">Value type.</typeparam>
public sealed record Map<TKey, TValue>(IReadOnlyList<KeyValuePair<TKey, TValue>> Entries)
{
    /// <summary>The number of entries in the map.</summary>
    public int Count => Entries.Count;

    /// <summary>
    /// Converts this map to its Ledger API record representation. The wrapping
    /// record has a single field named <c>map</c> carrying a
    /// <see cref="DamlGenMap"/>, matching the stdlib definition of
    /// <c>DA.Map.Types.Map</c> (and mirroring the shape used by
    /// <see cref="Set{T}"/>).
    /// </summary>
    public DamlRecord ToRecord(
        Func<TKey, DamlValue> convertKey,
        Func<TValue, DamlValue> convertValue)
    {
        ArgumentNullException.ThrowIfNull(convertKey);
        ArgumentNullException.ThrowIfNull(convertValue);
        var entries = Entries
            .Select(kv => ((DamlValue)convertKey(kv.Key), (DamlValue)convertValue(kv.Value)))
            .ToList();
        return DamlRecord.Create(
            DamlField.Create("map", new DamlGenMap(entries)));
    }

    /// <summary>
    /// Reconstructs a map from its Ledger API record representation.
    /// </summary>
    public static Map<TKey, TValue> FromRecord(
        DamlRecord record,
        Func<DamlValue, TKey> convertKey,
        Func<DamlValue, TValue> convertValue)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(convertKey);
        ArgumentNullException.ThrowIfNull(convertValue);
        var map = record.GetRequiredField("map").As<DamlGenMap>();
        var entries = map.Entries
            .Select(entry => new KeyValuePair<TKey, TValue>(
                convertKey(entry.Key),
                convertValue(entry.Value)))
            .ToList();
        return new Map<TKey, TValue>(entries);
    }
}

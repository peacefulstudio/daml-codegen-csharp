// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Data;

namespace Daml.Runtime.Stdlib;

/// <summary>
/// Daml stdlib type <c>DA.Set.Types.Set k</c> — a set of values.
/// </summary>
/// <remarks>
/// <para>
/// On the wire the type is a record with a single field <c>map : Map k Unit</c>:
/// a set is internally a Daml-LF generic map whose values are <see cref="DamlUnit"/>.
/// We expose <see cref="IReadOnlyCollection{T}"/> semantics so the consumer-facing
/// surface is the obvious one (iterate elements, count, contains-by-equality).
/// </para>
/// <para>
/// The C# codegen emits the type with a concrete CLR generic argument
/// (e.g. <c>Set&lt;Party&gt;</c>) which is not in general <see cref="IDamlRecord"/>.
/// Round-tripping therefore goes through caller-supplied converters that bridge
/// the generic CLR type to <see cref="DamlValue"/>; the codegen knows the
/// concrete element type at the call site and inlines the appropriate conversion
/// lambdas.
/// </para>
/// </remarks>
/// <typeparam name="T">Element type of the set.</typeparam>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Naming", "CA1716:Identifiers should not match keywords",
    Justification = "Matches the Daml stdlib type name DA.Set.Types.Set so the codegen-emitted type name resolves directly. Renaming would force the codegen to learn an additional translation.")]
public sealed record Set<T>
{
    /// <summary>The set's elements, deduplicated under default CLR equality.</summary>
    public IReadOnlySet<T> Elements { get; }

    /// <summary>
    /// Builds a set from an arbitrary input sequence. Duplicates under
    /// <see cref="EqualityComparer{T}.Default"/> are removed so the wire
    /// representation produced by <see cref="ToRecord"/> is always a valid
    /// <c>Map k Unit</c> with unique keys.
    /// </summary>
    public Set(IEnumerable<T> elements)
    {
        ArgumentNullException.ThrowIfNull(elements);
        Elements = elements as IReadOnlySet<T> ?? elements.ToHashSet();
    }

    /// <summary>The number of elements in the set.</summary>
    public int Count => Elements.Count;

    /// <summary>
    /// Returns true if the set contains <paramref name="element"/> under the
    /// default CLR equality for <typeparamref name="T"/>. Daml's set uses
    /// structural equality on the wire; for primitive element types and
    /// <see cref="Daml.Runtime.Data.Party"/> the two coincide, but for custom
    /// record element types consumers should rely on the record's own equality
    /// implementation.
    /// </summary>
    public bool Contains(T element) => Elements.Contains(element);

    /// <summary>
    /// Converts this set to its Ledger API record representation. The supplied
    /// <paramref name="convertElement"/> encodes each element to a <see cref="DamlValue"/>;
    /// elements are paired with <see cref="DamlUnit.Instance"/> in the inner map to match
    /// the Daml-LF wire shape <c>Set { map : Map k () }</c>.
    /// </summary>
    public DamlRecord ToRecord(Func<T, DamlValue> convertElement)
    {
        ArgumentNullException.ThrowIfNull(convertElement);
        var entries = Elements
            .Select(element => ((DamlValue)convertElement(element), (DamlValue)DamlUnit.Instance))
            .ToList();
        return DamlRecord.Create(
            DamlField.Create("map", new DamlGenMap(entries)));
    }

    /// <summary>
    /// Reconstructs a set from its Ledger API record representation. The supplied
    /// <paramref name="convertElement"/> decodes each element from its <see cref="DamlValue"/>
    /// form. Map values (which are always <see cref="DamlUnit"/>) are discarded;
    /// any structurally-equal duplicate keys on the wire are collapsed.
    /// </summary>
    public static Set<T> FromRecord(DamlRecord record, Func<DamlValue, T> convertElement)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(convertElement);
        var map = record.GetRequiredField("map").As<DamlGenMap>();
        var elements = map.Entries.Select(entry => convertElement(entry.Key));
        return new Set<T>(elements);
    }
}

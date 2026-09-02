// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Contracts;
using Daml.Runtime.Data;

namespace Daml.Runtime.Stdlib;

/// <summary>
/// Daml stdlib type <c>DA.NonEmpty.Types.NonEmpty a</c> — a list guaranteed to contain
/// at least one element.
/// </summary>
/// <remarks>
/// <para>
/// On the wire the type is a record with fields <c>hd : a</c> (the head) and
/// <c>tl : [a]</c> (the rest of the list). Iterating <see cref="All"/> yields
/// <see cref="Hd"/> followed by every element of <see cref="Tl"/>, so consumers
/// that just want the values can ignore the split.
/// </para>
/// <para>
/// The C# codegen emits the type with a concrete CLR generic argument
/// (e.g. <c>NonEmpty&lt;Party&gt;</c>) which is not in general <see cref="IDamlRecord"/>.
/// Round-tripping therefore goes through caller-supplied converters that bridge the
/// generic CLR type to <see cref="DamlValue"/>; the codegen knows the concrete
/// element type at the call site and inlines the appropriate conversion lambdas.
/// </para>
/// </remarks>
/// <typeparam name="T">Element type.</typeparam>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design",
    "CA1000:Do not declare static members on generic types",
    Justification = "The static factory is the wire-decoding entry point for this Daml stdlib shape; generated code calls it as NonEmpty<...>.FromRecord, mirroring the Daml constructor it decodes.")]
public sealed record NonEmpty<T>(T Hd, IReadOnlyList<T> Tl)
    where T : notnull
{
    private readonly IReadOnlyList<T> _tl = EventCollections.Copy(Tl, nameof(Tl));

    /// <summary>
    /// The elements after <see cref="Hd"/>. Copied at construction and on <c>init</c>, so a
    /// producer that retains the list it supplied cannot change this value's equality or
    /// hash code afterwards.
    /// </summary>
    public IReadOnlyList<T> Tl
    {
        get => _tl;
        init => _tl = EventCollections.Copy(value, nameof(Tl));
    }

    /// <summary>
    /// All elements: <see cref="Hd"/> first, followed by every element of <see cref="Tl"/>.
    /// </summary>
    public IEnumerable<T> All
    {
        get
        {
            yield return Hd;
            foreach (var item in Tl)
            {
                yield return item;
            }
        }
    }

    /// <summary>
    /// Converts this non-empty list to its Ledger API record representation.
    /// </summary>
    public DamlRecord ToRecord(Func<T, DamlValue> convertElement)
    {
        ArgumentNullException.ThrowIfNull(convertElement);
        var tail = Tl.Select(element => (DamlValue)convertElement(element)).ToList();
        return DamlRecord.Create(
            DamlField.Create("hd", convertElement(Hd)),
            DamlField.Create("tl", new DamlList(tail)));
    }

    /// <summary>
    /// Reconstructs a non-empty list from its Ledger API record representation.
    /// </summary>
    public static NonEmpty<T> FromRecord(DamlRecord record, Func<DamlValue, T> convertElement)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(convertElement);
        var hd = convertElement(record.GetRequiredField("hd"));
        var tl = record.GetRequiredField("tl").As<DamlList>().Values
            .Select(convertElement)
            .ToList();
        return new NonEmpty<T>(hd, tl);
    }

    /// <summary>
    /// Compares two non-empty lists by head and by tail content, in order. The
    /// record-synthesized equality compares the backing <see cref="IReadOnlyList{T}"/>
    /// by reference — a footgun for a value type — so we override it with structural
    /// comparison.
    /// </summary>
    public bool Equals(NonEmpty<T>? other) =>
        other is not null
        && EqualityComparer<T>.Default.Equals(Hd, other.Hd)
        && Tl.SequenceEqual(other.Tl);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Hd);
        hash.Add(Tl.Count);
        foreach (var item in Tl)
        {
            hash.Add(item);
        }
        return hash.ToHashCode();
    }
}

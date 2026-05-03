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
/// (e.g. <c>NonEmpty&lt;Party&gt;</c>) which is not in general <see cref="IDamlValue"/>.
/// Round-tripping therefore goes through caller-supplied converters that bridge the
/// generic CLR type to <see cref="DamlValue"/>; the codegen knows the concrete
/// element type at the call site and inlines the appropriate conversion lambdas.
/// </para>
/// </remarks>
/// <typeparam name="T">Element type.</typeparam>
public sealed record NonEmpty<T>(T Hd, IReadOnlyList<T> Tl)
{
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
}

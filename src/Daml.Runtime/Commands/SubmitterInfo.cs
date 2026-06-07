// Copyright (c) 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Frozen;
using Daml.Runtime.Data;

namespace Daml.Runtime.Commands;

/// <summary>
/// Identifies the parties on whose behalf a command submission is authorized.
/// Carries the <c>actAs</c> (authorizing) party set and the optional <c>readAs</c>
/// (read-only visibility) party set that propagate to <c>Commands.act_as</c> /
/// <c>Commands.read_as</c> in the Ledger API gRPC payload.
/// </summary>
/// <remarks>
/// <para>
/// At least one party must be present in <see cref="ActAs"/>; an empty set is rejected
/// with <see cref="ArgumentException"/>. <see cref="ReadAs"/> defaults to empty. Each
/// caller-supplied set is snapshotted into an immutable
/// <see cref="System.Collections.Frozen.FrozenSet{T}"/> at construction so caller
/// mutations after the fact don't bleed in, and so a consumer who casts
/// <see cref="ActAs"/> / <see cref="ReadAs"/> back to a concrete collection type
/// still cannot mutate the snapshot.
/// </para>
/// <para>
/// An implicit conversion from <see cref="Party"/> preserves the single-party
/// ergonomic at every existing call site. There is deliberately no
/// <see cref="string"/> conversion: callers construct parties explicitly with
/// <c>new Party(...)</c> so a bare string can never be mistaken for an authorized
/// submitter.
/// </para>
/// <para>
/// This is the canonical home of <c>SubmitterInfo</c>: <c>Party</c> already lives in
/// <c>Daml.Runtime</c>, so command submitters belong here too. The
/// <c>Canton.Ledger.Grpc.Client</c> package consumes this type via its
/// <c>Daml.Runtime</c> package reference once the <c>Daml.Runtime</c> version that
/// ships this type is in use.
/// </para>
/// </remarks>
public readonly record struct SubmitterInfo
{
    // FrozenSet.Empty is genuinely immutable — there is no underlying mutable
    // collection a consumer could cast back to and mutate, unlike a HashSet
    // typed as IReadOnlySet.
    private static readonly IReadOnlySet<Party> EmptyParties = FrozenSet<Party>.Empty;

    private readonly IReadOnlySet<Party>? _actAs;
    private readonly IReadOnlySet<Party>? _readAs;

    /// <summary>
    /// The set of parties on whose behalf the submission is authorized
    /// (<c>Commands.act_as</c>). Always non-empty.
    /// </summary>
    public IReadOnlySet<Party> ActAs => _actAs ?? throw new InvalidOperationException(
        "Cannot access ActAs of a default (uninitialized) SubmitterInfo. " +
        "Construct via the SubmitterInfo constructor or implicit conversion from Party.");

    /// <summary>
    /// The set of additional parties whose contracts are read-visible during command
    /// interpretation (<c>Commands.read_as</c>). Defaults to an empty set.
    /// </summary>
    public IReadOnlySet<Party> ReadAs => _readAs ?? EmptyParties;

    /// <summary>
    /// Creates a multi-party submitter set.
    /// </summary>
    /// <param name="actAs">The authorizing parties. Must be non-empty.</param>
    /// <param name="readAs">Optional read-only visibility parties. Defaults to empty.</param>
    /// <exception cref="ArgumentNullException"><paramref name="actAs"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException"><paramref name="actAs"/> is empty.</exception>
    public SubmitterInfo(IReadOnlySet<Party> actAs, IReadOnlySet<Party>? readAs = null)
    {
        ArgumentNullException.ThrowIfNull(actAs);
        if (actAs.Count == 0)
        {
            throw new ArgumentException(
                "SubmitterInfo.ActAs must contain at least one party.", nameof(actAs));
        }

        // Snapshot into a FrozenSet so the stored backing collection is genuinely
        // immutable. Callers that cast ActAs/ReadAs back to a concrete type still
        // can't mutate, and SubmitterInfo's value semantics survive any such cast.
        _actAs = ValidatedFrozenSet(actAs, nameof(actAs));
        _readAs = readAs is null || readAs.Count == 0
            ? null
            : ValidatedFrozenSet(readAs, nameof(readAs));
    }

    /// <summary>
    /// Creates a single-party submitter set — equivalent to constructing
    /// with a one-element set but avoids the intermediate collection allocation.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="singleActAs"/> is <c>default(Party)</c>.
    /// </exception>
    public SubmitterInfo(Party singleActAs, IReadOnlySet<Party>? readAs = null)
    {
        // Touch Id so a default(Party) submitter fails at construction rather
        // than later, when ToDamlValue / serialization would surface the
        // uninitialized state with a much harder-to-trace stack.
        _ = singleActAs.Id;
        _actAs = FrozenSet.Create(singleActAs);
        _readAs = readAs is null || readAs.Count == 0
            ? null
            : ValidatedFrozenSet(readAs, nameof(readAs));
    }

    private static FrozenSet<Party> ValidatedFrozenSet(IReadOnlySet<Party> source, string paramName)
    {
        foreach (var party in source)
        {
            // Touch Id on each party so default(Party) entries fail loudly at
            // construction. Mirrors the single-party ctor's invariant.
            try
            {
                _ = party.Id;
            }
            catch (InvalidOperationException ex)
            {
                throw new ArgumentException(
                    $"SubmitterInfo.{paramName} contains a default (uninitialized) Party.", paramName, ex);
            }
        }
        return source.ToFrozenSet();
    }

    /// <summary>
    /// Implicitly converts a single <see cref="Party"/> into a
    /// <see cref="SubmitterInfo"/> with that single <c>actAs</c> party and no
    /// <c>readAs</c> parties.
    /// </summary>
    public static implicit operator SubmitterInfo(Party singleActAs) =>
        new(singleActAs);

    /// <summary>
    /// Compares two <see cref="SubmitterInfo"/> instances by the contents of
    /// their <see cref="ActAs"/> and <see cref="ReadAs"/> sets. The
    /// record-struct-synthesized equality compares the backing
    /// <see cref="IReadOnlySet{T}"/> fields by reference — that's a footgun
    /// for a value type, so we override it explicitly here.
    /// </summary>
    public bool Equals(SubmitterInfo other) =>
        SetsEqual(_actAs, other._actAs) && SetsEqual(_readAs, other._readAs);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(
        SetHash(_actAs),
        SetHash(_readAs));

    private static bool SetsEqual(IReadOnlySet<Party>? a, IReadOnlySet<Party>? b)
    {
        var ac = a?.Count ?? 0;
        var bc = b?.Count ?? 0;
        if (ac != bc)
        {
            return false;
        }
        if (ac == 0)
        {
            return true;
        }
        // Both non-empty and same count — set-equal iff every element of `a` is in `b`.
        foreach (var p in a!)
        {
            if (!b!.Contains(p))
            {
                return false;
            }
        }
        return true;
    }

    private static int SetHash(IReadOnlySet<Party>? set)
    {
        if (set is null || set.Count == 0)
        {
            return 0;
        }
        // Order-independent: XOR each element's hash so set equality implies
        // hash equality regardless of enumeration order.
        var h = 0;
        foreach (var p in set)
        {
            h ^= p.GetHashCode();
        }
        return h;
    }
}

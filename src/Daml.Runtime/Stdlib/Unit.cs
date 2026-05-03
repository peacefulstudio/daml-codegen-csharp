// Copyright (c) 2026 Peaceful Studio OÜ. All rights reserved.

namespace Daml.Runtime.Stdlib;

/// <summary>
/// The Daml unit type — a single inhabitant, no payload.
/// </summary>
/// <remarks>
/// <para>
/// Mirrors the role of <see cref="System.ValueTuple"/> but participates in the
/// codegen's typed return-value path. Codegen emits <c>ExerciseOutcome&lt;Unit&gt;</c>
/// for choices declared as <c>choice Foo : ()</c>. <see cref="Value"/> is the only
/// reachable inhabitant: the constructor is private, the type is a sealed class
/// (not a record, to prevent <c>with</c>-expression clones), and equality is
/// overridden so any two references that survive the type system compare equal.
/// </para>
/// <para>
/// This is distinct from <c>Daml.Runtime.Data.DamlUnit</c>, which is the
/// wire-level <c>DamlValue</c> representation of unit. The codegen's typed
/// wrappers carry <see cref="Unit"/> at the call site and convert via
/// <c>DamlUnit.Instance</c> at the wire boundary.
/// </para>
/// </remarks>
public sealed class Unit : IEquatable<Unit>
{
    private Unit() { }

    /// <summary>The single inhabitant of <see cref="Unit"/>.</summary>
    public static Unit Value { get; } = new();

    /// <inheritdoc/>
    public bool Equals(Unit? other) => other is not null;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is Unit;

    /// <inheritdoc/>
    public override int GetHashCode() => 0;

    /// <summary>Two <see cref="Unit"/> references always compare equal.</summary>
    public static bool operator ==(Unit? left, Unit? right) => (left is null) == (right is null);

    /// <summary>Two <see cref="Unit"/> references always compare equal.</summary>
    public static bool operator !=(Unit? left, Unit? right) => !(left == right);
}

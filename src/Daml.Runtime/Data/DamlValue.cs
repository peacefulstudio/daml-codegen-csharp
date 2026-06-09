// Copyright (c) 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Daml.Runtime.Data;

/// <summary>
/// Marker interface for all Daml values that can be serialized to/from the Ledger API.
/// </summary>
/// <remarks>
/// Carries no members. Record-shaped values implement <see cref="IDamlRecord"/> and
/// variant-shaped values implement <see cref="IDamlVariant"/>; both extend this marker
/// so generic helpers can constrain on the broader type without dispatching on shape.
/// </remarks>
public interface IDamlValue
{
}

/// <summary>
/// A Daml value whose Ledger API representation is a record.
/// </summary>
public interface IDamlRecord : IDamlValue
{
    /// <summary>
    /// Converts this value to its Ledger API record representation.
    /// </summary>
    DamlRecord ToRecord();
}

/// <summary>
/// A Daml value whose Ledger API representation is a variant.
/// </summary>
public interface IDamlVariant : IDamlValue
{
    /// <summary>
    /// Converts this value to its Ledger API variant representation.
    /// </summary>
    DamlVariant ToVariant();
}

/// <summary>
/// Base class for all Daml primitive and composite values.
/// </summary>
public abstract record DamlValue
{
    /// <summary>
    /// Attempts to cast this value to the specified type.
    /// </summary>
    public T As<T>() where T : DamlValue =>
        this as T ?? throw new InvalidCastException($"Cannot cast {GetType().Name} to {typeof(T).Name}");

    /// <summary>
    /// Attempts to get the value as the specified primitive type.
    /// </summary>
    public bool TryGet<T>(out T? result) where T : DamlValue
    {
        if (this is T typed)
        {
            result = typed;
            return true;
        }
        result = default;
        return false;
    }
}

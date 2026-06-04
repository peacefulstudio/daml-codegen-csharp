// Copyright (c) 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Daml.Runtime.Data;

/// <summary>
/// Base interface for all Daml values that can be serialized to/from the Ledger API.
/// </summary>
public interface IDamlValue
{
    /// <summary>
    /// Converts this value to its Ledger API representation.
    /// </summary>
    DamlRecord ToRecord();
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

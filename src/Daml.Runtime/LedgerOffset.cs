// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System;

namespace Daml.Runtime;

/// <summary>
/// A position in the participant's ledger stream. Replaces raw <see cref="long"/>
/// offsets so callers use <see cref="Begin"/> instead of a magic <c>0</c>/<c>null</c>.
/// </summary>
public readonly record struct LedgerOffset
{
    private LedgerOffset(long value) => Value = value;

    /// <summary>The well-known participant start of stream.</summary>
    public static LedgerOffset Begin { get; } = new(0);

    /// <summary>A concrete offset at <paramref name="value"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is negative.</exception>
    public static LedgerOffset At(long value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        return new(value);
    }

    /// <summary>The underlying participant offset.</summary>
    public long Value { get; }
}

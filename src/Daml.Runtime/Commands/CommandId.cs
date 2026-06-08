// Copyright (c) 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Daml.Runtime.Commands;

/// <summary>
/// Deduplication identifier carried on a <see cref="CommandsSubmission"/> and projected
/// onto the Ledger API <c>command_id</c> field.
/// </summary>
/// <remarks>
/// Conversions to and from <see cref="string"/> are both explicit, so a command id can
/// never be silently mistaken for an arbitrary string (or vice versa) — in particular it
/// cannot be transposed with a <see cref="WorkflowId"/> at a call site; use
/// <see cref="Value"/> or <see cref="ToString"/> for logging and interpolation.
/// </remarks>
public readonly record struct CommandId
{
    private readonly string? _value;

    /// <summary>The verbatim command id string.</summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when accessed on a default-initialized value.
    /// </exception>
    public string Value =>
        _value ?? throw new InvalidOperationException("Cannot access Value of a default (uninitialized) CommandId.");

    /// <summary>Constructs a <see cref="CommandId"/> from a non-empty string.</summary>
    /// <param name="value">The command id; stored verbatim (non-null, non-whitespace).</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="value"/> is null, empty, or whitespace.</exception>
    public CommandId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, nameof(value));
        _value = value;
    }

    public static explicit operator string(CommandId id) =>
        id._value ?? throw new InvalidOperationException("Cannot convert a default (uninitialized) CommandId to string.");

    public static explicit operator CommandId(string value) => new(value);

    /// <remarks>
    /// Returns a sentinel — not a throw — for <c>default(CommandId)</c>: logging
    /// frameworks may invoke <c>ToString</c> on a captured value during exception
    /// handling, and a throw here would mask the original exception.
    /// </remarks>
    public override string ToString() => _value ?? "<uninitialized CommandId>";
}

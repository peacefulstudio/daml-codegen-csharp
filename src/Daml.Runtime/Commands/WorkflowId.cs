// Copyright (c) 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Daml.Runtime.Commands;

/// <summary>
/// Correlation identifier carried on a <see cref="CommandsSubmission"/> and projected
/// onto the Ledger API <c>workflow_id</c> field.
/// </summary>
/// <remarks>
/// Conversions to and from <see cref="string"/> are both explicit, so a workflow id can
/// never be silently mistaken for an arbitrary string (or vice versa) — in particular it
/// cannot be transposed with a <see cref="CommandId"/> at a call site; use
/// <see cref="Value"/> or <see cref="ToString"/> for logging and interpolation.
/// </remarks>
public readonly record struct WorkflowId
{
    private readonly string? _value;

    /// <summary>The verbatim workflow id string.</summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when accessed on a default-initialized value.
    /// </exception>
    public string Value =>
        _value ?? throw new InvalidOperationException("Cannot access Value of a default (uninitialized) WorkflowId.");

    /// <summary>Constructs a <see cref="WorkflowId"/> from a non-null string.</summary>
    /// <param name="value">
    /// The workflow id; stored verbatim. Empty and whitespace values are accepted because the
    /// Ledger API treats <c>workflow_id</c> as optional with no non-empty constraint.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="value"/> is null.</exception>
    public WorkflowId(string value)
    {
        ArgumentNullException.ThrowIfNull(value, nameof(value));
        _value = value;
    }

    public static explicit operator string(WorkflowId id) =>
        id._value ?? throw new InvalidOperationException("Cannot convert a default (uninitialized) WorkflowId to string.");

    public static explicit operator WorkflowId(string value) => new(value);

    /// <remarks>
    /// Returns a sentinel — not a throw — for <c>default(WorkflowId)</c>: logging
    /// frameworks may invoke <c>ToString</c> on a captured value during exception
    /// handling, and a throw here would mask the original exception.
    /// </remarks>
    public override string ToString() => _value ?? "<uninitialized WorkflowId>";
}

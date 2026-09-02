// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Outcomes;

namespace Daml.Ledger.Abstractions;

/// <summary>
/// Thrown by the throwing convenience wrappers in <see cref="Extensions.ThrowingExercise"/>
/// when the underlying <c>Try*</c> method yields a non-success outcome. Carries the
/// structured data of a <see cref="ExerciseOutcome{T}.DamlError"/> /
/// <see cref="ExerciseOutcome{T}.InfraError"/> outcome so catch sites keep access to
/// the detail the structured API exposes; a classified infrastructure failure carries its
/// <see cref="Category"/> alongside its <see cref="StatusCode"/>. Derives from
/// <see cref="InvalidOperationException"/> because the convenience wrappers
/// previously threw <see cref="InvalidOperationException"/> directly; the base
/// type preserves that catch contract.
/// </summary>
public sealed class LedgerOperationException : InvalidOperationException
{
    /// <summary>
    /// Canton error category when the failed outcome was a
    /// <see cref="ExerciseOutcome{T}.DamlError"/>, or when it was an
    /// <see cref="ExerciseOutcome{T}.InfraError"/> the transport classified without a
    /// structured Canton error attached; <c>null</c> when neither applies.
    /// </summary>
    public DamlErrorCategory? Category { get; }

    /// <summary>
    /// Canton built-in or Daml-defined error identifier when the failed outcome was a
    /// <see cref="ExerciseOutcome{T}.DamlError"/>; otherwise <c>null</c>.
    /// </summary>
    public string? ErrorId { get; }

    /// <summary>
    /// Structured detail from <c>ErrorInfo.metadata</c> when the failed outcome was a
    /// <see cref="ExerciseOutcome{T}.DamlError"/>; otherwise <c>null</c>.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; }

    /// <summary>
    /// Transport status code when the failed outcome was an
    /// <see cref="ExerciseOutcome{T}.InfraError"/>; otherwise <c>null</c>.
    /// </summary>
    public int? StatusCode { get; }

    /// <summary>
    /// Creates an exception for an outcome with no structured error payload
    /// (<c>None</c>, <c>Many</c>, or an unexpected outcome subtype).
    /// </summary>
    public LedgerOperationException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Creates an exception with no structured error payload that wraps the
    /// exception that caused the failure.
    /// </summary>
    public LedgerOperationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Creates an exception carrying a <see cref="ExerciseOutcome{T}.DamlError"/> outcome.
    /// </summary>
    public LedgerOperationException(
        string message,
        DamlErrorCategory category,
        string errorId,
        IReadOnlyDictionary<string, string> metadata)
        : base(message)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        Category = category;
        ErrorId = errorId;
        Metadata = metadata;
    }

    /// <summary>
    /// Creates an exception carrying an <see cref="ExerciseOutcome{T}.InfraError"/> outcome,
    /// its classification when the transport determined one, and the transport exception that
    /// caused it when available.
    /// </summary>
    public LedgerOperationException(
        string message,
        int statusCode,
        DamlErrorCategory? category = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        Category = category;
    }
}

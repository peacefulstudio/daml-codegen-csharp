// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Data;

namespace Daml.Runtime.Commands;

/// <summary>
/// A contract explicitly disclosed alongside a submission so that a party
/// without native visibility into it can still be authorized to act on it —
/// Daml 3.x explicit disclosure. The Canton ledger client owns mapping this
/// onto the gRPC <c>DisclosedContract</c> message; this type is transport-neutral.
/// </summary>
/// <param name="ContractId">The disclosed contract's identifier.</param>
/// <param name="TemplateId">The disclosed contract's template identifier.</param>
/// <param name="CreatedEventBlob">
/// The raw <c>created_event_blob</c> bytes from the gRPC <c>CreatedEvent</c>,
/// carried verbatim and opaque to this library — no encoding is imposed on callers.
/// </param>
public sealed record DisclosedContract(
    string ContractId,
    Identifier TemplateId,
    ReadOnlyMemory<byte> CreatedEventBlob)
{
    /// <summary>
    /// Compares <see cref="CreatedEventBlob"/> byte-for-byte, unlike the synthesized
    /// record equality, which compares only the memory segment's reference, offset,
    /// and length.
    /// </summary>
    public bool Equals(DisclosedContract? other) =>
        other is not null
        && ContractId == other.ContractId
        && TemplateId == other.TemplateId
        && CreatedEventBlob.Span.SequenceEqual(other.CreatedEventBlob.Span);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(ContractId);
        hash.Add(TemplateId);
        hash.AddBytes(CreatedEventBlob.Span);
        return hash.ToHashCode();
    }
}

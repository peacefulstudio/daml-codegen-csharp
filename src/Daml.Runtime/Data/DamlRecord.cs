// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Daml.Runtime.Data;

/// <summary>
/// Represents a Daml record (product type) - a collection of named fields.
/// </summary>
/// <param name="RecordId">Optional identifier for the record type.</param>
/// <param name="Fields">The fields of the record.</param>
public sealed record DamlRecord(
    Identifier? RecordId,
    IReadOnlyList<DamlField> Fields) : DamlValue
{
    /// <summary>
    /// Creates a record with the specified fields.
    /// </summary>
    public static DamlRecord Create(params DamlField[] fields) =>
        new(null, fields);

    /// <summary>
    /// Creates a record with a type identifier and the specified fields.
    /// </summary>
    public static DamlRecord Create(Identifier recordId, params DamlField[] fields) =>
        new(recordId, fields);

    /// <summary>
    /// Gets a field value by name.
    /// </summary>
    public DamlValue? GetField(string name) =>
        Fields.FirstOrDefault(f => f.Label == name)?.Value;

    /// <summary>
    /// Gets a required field value by name, throwing if not found.
    /// </summary>
    public DamlValue GetRequiredField(string name) =>
        GetField(name) ?? throw new InvalidOperationException($"Required field '{name}' not found in record.");

    /// <summary>
    /// Compares two records by <see cref="RecordId"/> and field-by-field content.
    /// The record-synthesized equality compares the backing
    /// <see cref="IReadOnlyList{T}"/> by reference — a footgun for a value type —
    /// so we override it with structural element comparison.
    /// </summary>
    public bool Equals(DamlRecord? other) =>
        other is not null
        && Equals(RecordId, other.RecordId)
        && Fields.SequenceEqual(other.Fields);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(RecordId);
        foreach (var field in Fields)
        {
            hash.Add(field);
        }
        return hash.ToHashCode();
    }
}

/// <summary>
/// Represents a single field within a Daml record.
/// </summary>
/// <param name="Label">The field name.</param>
/// <param name="Value">The field value.</param>
public sealed record DamlField(string Label, DamlValue Value)
{
    /// <summary>
    /// Creates a field with the specified label and value.
    /// </summary>
    public static DamlField Create(string label, DamlValue value) => new(label, value);
}

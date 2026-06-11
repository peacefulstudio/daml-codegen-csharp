// Copyright (c) 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Data;

namespace Daml.Runtime.Stdlib;

/// <summary>
/// Daml stdlib type DA.Time.Types.RelTime — relative time as microseconds.
/// Hand-coded into Daml.Runtime since daml-stdlib-DA-Time-Types 1.0.0 is a frozen
/// stdlib package (no NuGet equivalent) referenced by many Splice DARs.
/// </summary>
public sealed record RelTime(long Microseconds) : IDamlRecord
{
    /// <summary>Encodes this RelTime as the single-field <c>microseconds</c> wire record.</summary>
    public DamlRecord ToRecord() => DamlRecord.Create(
        DamlField.Create("microseconds", new DamlInt64(Microseconds))
    );

    /// <summary>Decodes a RelTime from its <c>microseconds</c> wire record.</summary>
    public static RelTime FromRecord(DamlRecord record) => new(
        Microseconds: record.GetRequiredField("microseconds").As<DamlInt64>().Value
    );
}

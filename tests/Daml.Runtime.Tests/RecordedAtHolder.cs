// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Serialization;

namespace Daml.Runtime.Tests;

public sealed record RecordedAtHolder([property: DamlFieldAttribute("recordedAt")] DateTimeOffset RecordedAt)
    : IDamlRecord<RecordedAtHolder>
{
    public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create("recordedAt", new DamlTimestamp(RecordedAt)));

    public static RecordedAtHolder FromRecord(DamlRecord record) =>
        new(record.GetRequiredField("recordedAt").As<DamlTimestamp>().Value);

    public static DamlRecord __ReadDamlLfJson(JsonElement json, DamlLfJsonDecodeContext context)
    {
        DamlLfJsonDecoders.RequireObject(json, context);
        return DamlRecord.Create(DamlField.Create("recordedAt", DamlLfJsonDecoders.ReadTimestamp(
            DamlLfJsonDecoders.RequireField(json, context, "recordedAt"), context.Field("recordedAt"))));
    }
}

// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Contracts;
using Daml.Runtime.Data;

namespace Daml.Runtime.Tests;

public sealed record RecordedAtHolder([property: DamlFieldAttribute("recordedAt")] DateTimeOffset RecordedAt) : IDamlRecord
{
    public DamlRecord ToRecord() => DamlRecord.Create(DamlField.Create("recordedAt", new DamlTimestamp(RecordedAt)));
}

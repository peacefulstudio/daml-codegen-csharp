// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Data;

namespace Daml.Runtime.Tests;

internal static class DamlRecordFacet
{
    internal static T Materialize<T>(DamlRecord record) where T : IDamlRecord<T> =>
        T.FromRecord(record);
}

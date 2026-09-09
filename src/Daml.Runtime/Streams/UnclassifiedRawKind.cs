// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Daml.Runtime.Streams;

internal static class UnclassifiedRawKind
{
    internal const string EventSubject = "event";

    internal const string SnapshotRowSubject = "snapshot row";

    internal static string? Validated(
        UnclassifiedKind kind,
        string? rawKind,
        string subject,
        string rawKindParameterName) =>
        (kind, rawKind) switch
        {
            (UnclassifiedKind.Unknown, null) => throw new ArgumentException(
                $"An Unclassified {subject} with Kind Unknown must carry the transport's raw descriptor in RawKind.",
                rawKindParameterName),
            (not UnclassifiedKind.Unknown, not null) => throw new ArgumentException(
                $"An Unclassified {subject} with the enumerated Kind '{kind}' must not carry a RawKind; RawKind is populated only for Unknown.",
                rawKindParameterName),
            _ => rawKind,
        };
}

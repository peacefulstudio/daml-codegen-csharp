// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System;
using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;

namespace Daml.Ledger.Abstractions.Testing.Conformance.Tests;

public sealed record ConformanceProbe(string Owner) : ITemplate
{
    public static Identifier TemplateId { get; } = new("pkg", "M", "ConformanceProbe");
    public static string PackageId => "pkg";
    public static string PackageName => "conformance";
    public static Version PackageVersion { get; } = new(0, 1, 0);
    public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);

    public DamlRecord ToRecord() => DamlRecord.Create();
}

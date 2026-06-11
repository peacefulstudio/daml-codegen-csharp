// Copyright (c) 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Data;
using FluentAssertions;
using Daml.Codegen.Testing.Conformance.Richtypes;
using Xunit;

namespace Daml.Codegen.Testing.Conformance.Tests;

public class ProfileRoundTripTests
{
    [Fact]
    public void to_record_emits_fields_in_declaration_order_with_typed_values()
    {
        var profile = new Profile("ace", 7);

        var record = profile.ToRecord();

        record.Fields.Select(f => f.Label).Should().Equal("nickname", "level");
        record.GetRequiredField("nickname").As<DamlText>().Value.Should().Be("ace");
        record.GetRequiredField("level").As<DamlInt64>().Value.Should().Be(7);
    }

    [Fact]
    public void from_record_reconstructs_the_original_value()
    {
        var original = new Profile("ace", 7);

        var restored = Profile.FromRecord(original.ToRecord());

        restored.Should().Be(original);
    }
}

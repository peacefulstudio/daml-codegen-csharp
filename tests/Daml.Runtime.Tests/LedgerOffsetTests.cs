// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System;
using AwesomeAssertions;
using Daml.Runtime;
using Xunit;

namespace Daml.Runtime.Tests;

public sealed class LedgerOffsetTests
{
    [Fact]
    public void Begin_has_value_zero()
    {
        LedgerOffset.Begin.Value.Should().Be(0);
    }

    [Fact]
    public void At_stores_the_given_value()
    {
        LedgerOffset.At(42).Value.Should().Be(42);
    }

    [Fact]
    public void At_rejects_negative_offset()
    {
        Action act = () => LedgerOffset.At(-1);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Offsets_with_the_same_value_are_equal()
    {
        LedgerOffset.At(7).Should().Be(LedgerOffset.At(7));
        LedgerOffset.Begin.Should().Be(LedgerOffset.At(0));
    }
}

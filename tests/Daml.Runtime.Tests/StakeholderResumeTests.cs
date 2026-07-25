// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Daml.Runtime;
using Xunit;

namespace Daml.Runtime.Tests;

public sealed class StakeholderResumeTests
{
    [Fact]
    public void StakeholderResume_stores_the_given_offset()
    {
        var resume = new StakeholderResume(LedgerOffset.At(42));
        resume.Offset.Should().Be(LedgerOffset.At(42));
    }

    [Fact]
    public void StakeholderResume_with_the_same_offset_is_equal()
    {
        new StakeholderResume(LedgerOffset.At(7)).Should().Be(new StakeholderResume(LedgerOffset.At(7)));
    }

    [Fact]
    public void StakeholderResume_with_a_different_offset_is_not_equal()
    {
        new StakeholderResume(LedgerOffset.At(7)).Should().NotBe(new StakeholderResume(LedgerOffset.At(8)));
    }
}

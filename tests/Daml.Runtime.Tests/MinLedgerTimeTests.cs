// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Commands;
using AwesomeAssertions;
using Xunit;

namespace Daml.Runtime.Tests;

public class MinLedgerTimeTests
{
    [Fact]
    public void Absolute_should_carry_the_instant_verbatim()
    {
        var instant = new DateTimeOffset(2026, 8, 15, 9, 30, 0, TimeSpan.FromHours(2));

        var bound = new MinLedgerTime.Absolute(instant);

        bound.Value.Should().Be(instant);
        bound.Value.Offset.Should().Be(TimeSpan.FromHours(2));
    }

    [Fact]
    public void Relative_should_carry_the_delay_verbatim()
    {
        var bound = new MinLedgerTime.Relative(TimeSpan.FromSeconds(30));

        bound.Value.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void Relative_should_accept_a_zero_delay()
    {
        var bound = new MinLedgerTime.Relative(TimeSpan.Zero);

        bound.Value.Should().Be(TimeSpan.Zero);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-30)]
    public void Relative_should_throw_when_the_delay_is_negative(int seconds)
    {
        Action act = () => _ = new MinLedgerTime.Relative(TimeSpan.FromSeconds(seconds));

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Absolute_should_compare_by_instant()
    {
        var instant = new DateTimeOffset(2026, 8, 15, 9, 30, 0, TimeSpan.Zero);

        new MinLedgerTime.Absolute(instant).Should().Be(new MinLedgerTime.Absolute(instant));
        new MinLedgerTime.Absolute(instant)
            .Should().NotBe(new MinLedgerTime.Absolute(instant.AddSeconds(1)));
    }

    [Fact]
    public void Relative_should_compare_by_delay()
    {
        new MinLedgerTime.Relative(TimeSpan.FromSeconds(30))
            .Should().Be(new MinLedgerTime.Relative(TimeSpan.FromSeconds(30)));
        new MinLedgerTime.Relative(TimeSpan.FromSeconds(30))
            .Should().NotBe(new MinLedgerTime.Relative(TimeSpan.FromSeconds(31)));
    }

    [Fact]
    public void Match_should_apply_the_handler_for_the_arm_it_carries()
    {
        MinLedgerTime absolute = new MinLedgerTime.Absolute(
            new DateTimeOffset(2026, 8, 15, 9, 30, 0, TimeSpan.Zero));
        MinLedgerTime relative = new MinLedgerTime.Relative(TimeSpan.FromSeconds(30));

        Describe(absolute).Should().Be("abs:2026-08-15T09:30:00.0000000+00:00");
        Describe(relative).Should().Be("rel:00:00:30");
    }

    [Fact]
    public void Match_should_leave_the_other_arms_handler_unapplied()
    {
        var absoluteApplied = 0;
        var relativeApplied = 0;

        _ = new MinLedgerTime.Relative(TimeSpan.FromSeconds(30)).Match(
            _ => ++absoluteApplied,
            _ => ++relativeApplied);

        absoluteApplied.Should().Be(0);
        relativeApplied.Should().Be(1);
    }

    [Fact]
    public void Match_should_throw_when_a_handler_is_null()
    {
        MinLedgerTime bound = new MinLedgerTime.Relative(TimeSpan.FromSeconds(30));

        Action withoutAbsolute = () => bound.Match(null!, _ => "rel");
        Action withoutRelative = () => bound.Match(_ => "abs", null!);

        withoutAbsolute.Should().Throw<ArgumentNullException>();
        withoutRelative.Should().Throw<ArgumentNullException>();

        MinLedgerTime absoluteBound = new MinLedgerTime.Absolute(
            new DateTimeOffset(2026, 8, 15, 9, 30, 0, TimeSpan.Zero));

        Action absoluteWithoutAbsoluteHandler = () => absoluteBound.Match(null!, _ => "rel");
        Action absoluteWithoutRelativeHandler = () => absoluteBound.Match(_ => "abs", null!);

        absoluteWithoutAbsoluteHandler.Should().Throw<ArgumentNullException>();
        absoluteWithoutRelativeHandler.Should().Throw<ArgumentNullException>();
    }

    private static string Describe(MinLedgerTime bound) =>
        bound.Match(
            absolute => $"abs:{absolute:O}",
            relative => $"rel:{relative}");
}

using Daml.Runtime.Data;
using Daml.Runtime.Stdlib;
using FluentAssertions;
using Xunit;

namespace Daml.Runtime.Tests;

/// <summary>
/// Tests for the hand-coded Daml.Runtime.Stdlib types — the stand-ins for Daml
/// stdlib types that we cannot, or do not want to, generate per consumer package.
/// </summary>
public class StdlibTests
{
    #region RelTime

    [Fact]
    public void RelTime_should_round_trip_through_ToRecord_FromRecord()
    {
        // Arrange
        var original = new RelTime(60_000_000); // 60 seconds, in microseconds

        // Act
        var record = original.ToRecord();
        var recovered = RelTime.FromRecord(record);

        // Assert
        recovered.Should().Be(original);
        recovered.Microseconds.Should().Be(60_000_000);
    }

    [Fact]
    public void RelTime_ToRecord_uses_microseconds_field_name()
    {
        // The Daml stdlib type is `RelTime { microseconds : Int }`. The field name
        // must match the wire shape exactly — anything else would fail to round-trip
        // through DamlRecord-encoded payloads coming from the ledger.
        var rel = new RelTime(123);

        var record = rel.ToRecord();

        record.Fields.Should().HaveCount(1);
        record.Fields[0].Label.Should().Be("microseconds");
        record.Fields[0].Value.Should().BeOfType<DamlInt64>().Which.Value.Should().Be(123L);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(1L)]
    [InlineData(-1L)]                  // Daml RelTime is signed; negatives represent "in the past".
    [InlineData(long.MaxValue)]
    [InlineData(long.MinValue)]
    public void RelTime_should_round_trip_signed_int64_boundaries(long microseconds)
    {
        var rel = new RelTime(microseconds);

        var recovered = RelTime.FromRecord(rel.ToRecord());

        recovered.Microseconds.Should().Be(microseconds);
    }

    [Fact]
    public void RelTime_FromRecord_should_throw_when_microseconds_field_missing()
    {
        var emptyRecord = DamlRecord.Create();

        var act = () => RelTime.FromRecord(emptyRecord);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*microseconds*");
    }

    #endregion

    #region GenericStub

    [Fact]
    public void GenericStub_NotImplemented_should_throw_with_context_in_message()
    {
        var act = () => GenericStub.NotImplemented<string>("amulet");

        act.Should().Throw<NotImplementedException>()
            .WithMessage("*amulet*")
            .And.Message.Should().Contain("issues/57",
                because: "the message must point at the tracking issue so consumers can find the workaround status");
    }

    [Fact]
    public void GenericStub_NotImplemented_should_throw_for_any_type_parameter()
    {
        // The signature returns T so the call can sit in expression position. Verify
        // the throw is unconditional regardless of the type parameter.
        var actString = () => GenericStub.NotImplemented<string>("ctx");
        var actInt = () => GenericStub.NotImplemented<int>("ctx");
        var actRecord = () => GenericStub.NotImplemented<DamlRecord>("ctx");

        actString.Should().Throw<NotImplementedException>();
        actInt.Should().Throw<NotImplementedException>();
        actRecord.Should().Throw<NotImplementedException>();
    }

    #endregion
}

// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using AwesomeAssertions;
using Daml.Codegen.Testing.Conformance.RichTypes;
using Daml.Runtime.Data;
using Daml.Runtime.Serialization;
using Xunit;

namespace Daml.Codegen.Testing.Conformance.Tests;

/// <summary>
/// Invokes the emitted <c>ArgumentDecoder</c>/<c>ArgumentJsonDecoder</c> delegates for the
/// synthetic <c>Archive</c> choice against real <see cref="DamlValue"/>s, on both a
/// template-owned choice (<see cref="Marker"/>) and an interface-owned one
/// (<see cref="IHolding"/>). Unlike <c>ArchiveViaContractIdTests</c>, which pins the
/// <c>ArgumentEncoder</c> shape, these pin the decode direction: a wire value that is
/// neither the empty-record shape <see cref="ArchiveViaContractIdTests"/> encodes nor a
/// bare <see cref="DamlUnit"/> must fail with the same <see cref="InvalidOperationException"/>
/// regardless of which of those two shapes the input resembles, rather than an
/// <see cref="InvalidCastException"/> leaking out of the record cast the decoder is built on.
/// </summary>
public class ArchiveArgumentDecoderTests
{
    [Fact]
    public void ArchiveArgumentDecoder_on_a_template_choice_decodes_an_empty_record_to_Unit()
    {
        var result = Marker.ChoiceArchive.ArgumentDecoder(DamlRecord.Create());

        result.Should().Be(DamlUnit.Instance);
    }

    [Fact]
    public void ArchiveArgumentDecoder_on_a_template_choice_rejects_a_non_empty_record()
    {
        var act = () => Marker.ChoiceArchive.ArgumentDecoder(DamlRecord.Create(DamlField.Create("unexpected", DamlUnit.Instance)));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Choice 'Archive' argument must decode to an empty record.");
    }

    [Fact]
    public void ArchiveArgumentDecoder_on_a_template_choice_rejects_a_DamlUnit_input_with_InvalidOperationException()
    {
        var act = () => Marker.ChoiceArchive.ArgumentDecoder(DamlUnit.Instance);

        act.Should().Throw<InvalidOperationException>(
                "a DamlUnit input is not the empty-record shape Archive's argument must decode to, and must not leak the record cast's InvalidCastException instead")
            .WithMessage("Choice 'Archive' argument must decode to an empty record.");
    }

    [Fact]
    public void ArchiveArgumentDecoder_on_a_template_choice_rejects_a_DamlText_input_with_InvalidOperationException()
    {
        var act = () => Marker.ChoiceArchive.ArgumentDecoder(new DamlText("not-a-record"));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Choice 'Archive' argument must decode to an empty record.");
    }

    [Fact]
    public void ArchiveArgumentDecoder_on_an_interface_choice_rejects_a_DamlUnit_input_with_InvalidOperationException()
    {
        var act = () => IHolding.ChoiceArchive.ArgumentDecoder(DamlUnit.Instance);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Choice 'Archive' argument must decode to an empty record.");
    }

    [Fact]
    public void ArchiveArgumentJsonDecoder_tolerates_a_non_empty_object()
    {
        using var document = JsonDocument.Parse("""{"unexpected":1}""");

        var result = Marker.ChoiceArchive.ArgumentJsonDecoder(document.RootElement, DamlLfJsonDecodeContext.Root("Archive"));

        result.Should().Be(DamlUnit.Instance);
    }

    [Fact]
    public void ArchiveArgumentJsonDecoder_rejects_a_non_object()
    {
        using var document = JsonDocument.Parse("\"not-an-object\"");

        var act = () => Marker.ChoiceArchive.ArgumentJsonDecoder(document.RootElement, DamlLfJsonDecodeContext.Root("Archive"));

        act.Should().Throw<JsonException>()
            .WithMessage("Expected JSON Object at 'Archive' but found String");
    }
}

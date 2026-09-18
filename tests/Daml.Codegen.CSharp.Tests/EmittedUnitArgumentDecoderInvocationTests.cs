// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Reflection;
using AwesomeAssertions;
using Daml.Runtime.Commands;
using Daml.Runtime.Data;
using Xunit;
using static Daml.Codegen.CSharp.Tests.EmittedCodeCompilesTestHelpers;

namespace Daml.Codegen.CSharp.Tests;

/// <summary>
/// Invokes the emitted <c>ArgumentDecoder</c> for a genuine <c>Unit</c>-argument choice —
/// as opposed to the synthetic <c>Archive</c> choice's empty-record shape — through
/// reflection over a compiled assembly, since no corpus type in
/// <c>Daml.Codegen.Testing.Conformance</c> declares a real Daml choice with a bare
/// <c>Unit</c> argument (every argument-less choice there is the synthesized
/// <c>Archive</c>). <see cref="EmittedCodeCompilesTestHelpers.GenerateKeyBearingTemplate"/>'s
/// <c>Reissue</c> choice is declared with a <see cref="Daml.Codegen.Intermediate.Model.DamlPrimitive.Unit"/>
/// argument, so its emitted decoder takes the non-synthetic branch of
/// <c>ChoiceEmitter.WriteEmptyArgumentDecoder</c> — <c>val is DamlUnit u ? u : throw ...</c> —
/// which the record-cast bug tracked in <c>ArchiveArgumentDecoderTests</c> never affected, but
/// which no prior test executed rather than string-matched.
/// </summary>
public class EmittedUnitArgumentDecoderInvocationTests
{
    private static readonly Assembly Emitted = EmitToAssembly(GenerateKeyBearingTemplate());

    private static IChoice ReissueChoice =>
        (IChoice)EmittedType("AssetWithKey")
            .GetProperty("ChoiceReissue", BindingFlags.Public | BindingFlags.Static)!
            .GetValue(null)!;

    [Fact]
    public void EmittedUnitArgumentDecoder_decodes_DamlUnit_to_Unit()
    {
        var result = ReissueChoice.DecodeArgument(DamlUnit.Instance);

        result.Should().Be(DamlUnit.Instance);
    }

    [Fact]
    public void EmittedUnitArgumentDecoder_rejects_a_non_Unit_input_with_InvalidOperationException()
    {
        var act = () => ReissueChoice.DecodeArgument(DamlRecord.Create());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Choice 'Reissue' argument must decode to DamlUnit.");
    }

    private static Type EmittedType(string name) => Emitted.GetTypes().Single(t => t.Name == name);
}

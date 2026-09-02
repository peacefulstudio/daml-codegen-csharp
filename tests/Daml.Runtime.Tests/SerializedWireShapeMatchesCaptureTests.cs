// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using AwesomeAssertions;
using Daml.Runtime.Data;
using Daml.Runtime.Serialization;
using Xunit;

namespace Daml.Runtime.Tests;

/// <summary>
/// Holds the writer answerable to real participant output: every capture the typed reader decodes
/// is read back and re-serialized, and the result must be the payload the participant sent.
/// </summary>
public class SerializedWireShapeMatchesCaptureTests
{
    [Theory]
    [MemberData(nameof(DecodedWireSamplePayloads))]
    public void Serialize_should_reproduce_the_captured_payload(
        string fileName, string payloadPath, string shapeName)
    {
        using var document = DamlLfJsonReaderWireSamplesTests.LoadWireSample(fileName);
        var captured = DamlLfJsonReaderWireSamplesTests.ResolvePayload(document.RootElement, payloadPath);

        var record = DamlLfJsonReader.ReadRecord(
            captured, DamlLfJsonReaderWireSamplesTests.DeclaredShapes[shapeName]);
        using var written = JsonDocument.Parse(DamlJsonSerializer.Serialize(record));

        DisagreementsBetween(captured, written.RootElement, shapeName).Should().BeEmpty(
            "the participant's own payload is the wire grammar the writer has to speak");
    }

    /// <summary>
    /// A capture whose payloads the typed reader cannot decode, so the writer cannot be held
    /// against it. Naming one here is the only way to leave it out of the gate below.
    /// </summary>
    private static readonly string[] CapturesCarryingNoDecodableRecord =
    [
        "probe_nested_optional_matrix.json",
    ];

    /// <summary>
    /// Anchored on the corpus directory, not on the member this class's theory data is projected
    /// from: comparing a projection against its own source can never fail. Payload-level totality
    /// is structural instead — <see cref="DecodedWireSamplePayloads"/> loops over every supported
    /// row with no filter — so what needs a gate is a capture landing on disk that no writer
    /// assertion ever reaches.
    /// </summary>
    [Fact]
    public void SerializedWireShapeMatchesCapture_should_hold_the_writer_against_every_capture_on_disk()
    {
        var capturesTheWriterIsHeldAgainst = FileNamesOf(DecodedWireSamplePayloads).Distinct();

        capturesTheWriterIsHeldAgainst.Concat(CapturesCarryingNoDecodableRecord)
            .Should().BeEquivalentTo(
                Directory.EnumerateFiles(DamlLfJsonReaderWireSamplesTests.CorpusDirectory, "*.json")
                    .Select(Path.GetFileName),
                "a capture the writer is never held against would let the two halves of the "
                + "grammar drift apart again");
    }

    public static TheoryData<string, string, string> DecodedWireSamplePayloads
    {
        get
        {
            var payloads = new TheoryData<string, string, string>();
            foreach (ITheoryDataRow row in DamlLfJsonReaderWireSamplesTests.SupportedWireSamplePayloads)
            {
                var data = row.GetData();
                payloads.Add((string)data[0]!, (string)data[1]!, (string)data[2]!);
            }
            return payloads;
        }
    }

    private static IEnumerable<string> FileNamesOf(IEnumerable<ITheoryDataRow> rows) =>
        rows.Select(row => (string)row.GetData()[0]!);

    private static IReadOnlyList<string> DisagreementsBetween(
        JsonElement captured, JsonElement written, string path)
    {
        if (captured.ValueKind != written.ValueKind)
        {
            return [$"{path}: captured {captured.ValueKind} '{captured.GetRawText()}' "
                + $"but wrote {written.ValueKind} '{written.GetRawText()}'"];
        }

        return captured.ValueKind switch
        {
            JsonValueKind.Object => ObjectDisagreements(captured, written, path),
            JsonValueKind.Array => ArrayDisagreements(captured, written, path),
            JsonValueKind.String => StringDisagreements(captured, written, path),
            _ => captured.GetRawText() == written.GetRawText()
                ? []
                : [$"{path}: captured '{captured.GetRawText()}' but wrote '{written.GetRawText()}'"],
        };
    }

    private static IReadOnlyList<string> ObjectDisagreements(
        JsonElement captured, JsonElement written, string path)
    {
        var capturedNames = captured.EnumerateObject().Select(property => property.Name).ToList();
        var writtenNames = written.EnumerateObject().Select(property => property.Name).ToList();
        if (capturedNames.Count != writtenNames.Count || capturedNames.Except(writtenNames).Any())
        {
            return [$"{path}: captured fields [{string.Join(", ", capturedNames)}] "
                + $"but wrote [{string.Join(", ", writtenNames)}]"];
        }

        return [.. capturedNames.SelectMany(name => DisagreementsBetween(
            captured.GetProperty(name), written.GetProperty(name), $"{path}.{name}"))];
    }

    private static IReadOnlyList<string> ArrayDisagreements(
        JsonElement captured, JsonElement written, string path)
    {
        if (captured.GetArrayLength() != written.GetArrayLength())
        {
            return [$"{path}: captured {captured.GetArrayLength()} elements "
                + $"but wrote {written.GetArrayLength()}"];
        }

        return [.. captured.EnumerateArray().Zip(written.EnumerateArray())
            .SelectMany((pair, index) =>
                DisagreementsBetween(pair.First, pair.Second, $"{path}[{index}]"))];
    }

    /// <summary>
    /// A participant pads a Numeric out to its declared scale; the declared scale is not on the
    /// wire, so the writer emits the canonical unpadded form of the same value. Every other
    /// string-valued arm has to reproduce the capture verbatim.
    /// </summary>
    private static IReadOnlyList<string> StringDisagreements(
        JsonElement captured, JsonElement written, string path)
    {
        var capturedText = captured.GetString()!;
        var writtenText = written.GetString()!;
        if (capturedText == writtenText)
        {
            return [];
        }

        var agreesAsNumeric = DamlNumeric.TryParseCanonical(capturedText, out var capturedNumeric)
            && DamlNumeric.TryParseCanonical(writtenText, out var writtenNumeric)
            && capturedNumeric.Equals(writtenNumeric);

        return agreesAsNumeric ? [] : [$"{path}: captured '{capturedText}' but wrote '{writtenText}'"];
    }
}

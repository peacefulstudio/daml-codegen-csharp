// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.RegularExpressions;
using AwesomeAssertions;
using Xunit;

namespace Daml.Codegen.CSharp.Tests;

/// <summary>
/// Regression coverage for the mirror-promotion content gates: the promotion
/// overlay's leak-check runs this same bare-issue-reference pattern (plus an
/// allowed-cross-repo-form filter) and this same internal-only-MSBuild-package
/// pattern. Pins both patterns against representative lines so a future edit to
/// either gate can't silently regress to matching the pre-fix state. The
/// bare-issue-reference character class is written out as ASCII literals rather
/// than as <c>\w</c> so that both engines agree: POSIX ERE, which the shell
/// gate's <c>grep -E</c> implements, has no <c>\w</c> escape inside a bracket
/// expression and reads it as the two literal characters backslash and w, which
/// makes <c>[^\w]</c> mean something quite different there than it does here.
/// This test's own "should be blocked" fixtures are built at runtime (never as a
/// contiguous literal in this file's source) precisely so this shipped test file
/// can never itself trip the gates it verifies.
/// </summary>
public class MirrorPromotionGateTests
{
    private static readonly Regex BareIssueReference = new(@"(^|[^A-Za-z0-9_])#[0-9]+", RegexOptions.Multiline);
    private static readonly Regex AllowedCrossRepoReference = new(@"(canton|grpc/grpc|digital-asset/daml)#[0-9]+");
    private static readonly Regex InternalOnlyMsBuildPackage = new(@"Daml\.Codegen\.CSharp\.MSBuild");

    private static bool IsBlockedByDeadRefSweep(string line)
    {
        var strippedOfAllowedForms = AllowedCrossRepoReference.Replace(line, string.Empty);
        return BareIssueReference.IsMatch(strippedOfAllowedForms);
    }

    private static string BareReferenceExample(string prefix, string number, string suffix) =>
        string.Concat(prefix, "#", number, suffix);

    [Theory]
    [InlineData("this has a bad ", "529", " ref")]
    [InlineData("mid sentence (", "397", ") regression")]
    [InlineData("remains tracked in ", "64", "; see the follow-up note")]
    [InlineData("(", "455", ")")]
    public void MirrorPromotionGate_bare_issue_reference_is_blocked_by_the_dead_ref_sweep(string prefix, string number, string suffix)
    {
        var line = BareReferenceExample(prefix, number, suffix);

        IsBlockedByDeadRefSweep(line).Should().BeTrue();
    }

    [Theory]
    [InlineData("driven by the downstream consumer (canton#190)")]
    [InlineData("workaround for grpc/grpc#38538 — bundled Grpc.Tools linux_arm64 protoc")]
    [InlineData("upstream fix tracked in digital-asset/daml#123")]
    [InlineData("no reference at all in this line")]
    public void MirrorPromotionGate_allowed_cross_repo_reference_passes_the_dead_ref_sweep(string line)
    {
        IsBlockedByDeadRefSweep(line).Should().BeFalse();
    }

    [Theory]
    [InlineData("driven by the downstream consumer (canton#190) — also ", "529")]
    [InlineData("workaround for grpc/grpc#38538 and closes ", "529")]
    [InlineData("upstream fix tracked in digital-asset/daml#123, follow-up in ", "397")]
    public void MirrorPromotionGate_bare_ref_sharing_a_line_with_an_allowed_cross_repo_form_is_still_blocked(string prefix, string number)
    {
        var line = prefix + BareReferenceExample("", number, "");

        IsBlockedByDeadRefSweep(line).Should().BeTrue();
    }

    [Theory]
    [InlineData("New `", "` package: add it and declare a DamlArchive item")]
    [InlineData("dotnet add package ", "")]
    public void MirrorPromotionGate_internal_only_msbuild_package_is_blocked_by_the_530_guard(string prefix, string suffix)
    {
        var packageName = string.Concat("Daml.Codegen.CSharp", ".", "MSBuild");
        var line = string.Concat(prefix, packageName, suffix);

        InternalOnlyMsBuildPackage.IsMatch(line).Should().BeTrue();
    }

    [Fact]
    public void MirrorPromotionGate_the_already_public_emitter_package_passes_the_530_guard()
    {
        InternalOnlyMsBuildPackage.IsMatch("dotnet add package Daml.Codegen.CSharp").Should().BeFalse();
    }

    [Theory]
    [InlineData("trailing_", "123", "")]
    [InlineData("adjacentX", "123", "")]
    [InlineData("adjacent9", "123", "")]
    public void MirrorPromotionGate_ascii_word_character_before_the_reference_passes_the_dead_ref_sweep(string prefix, string number, string suffix)
    {
        var line = BareReferenceExample(prefix, number, suffix);

        IsBlockedByDeadRefSweep(line).Should().BeFalse();
    }

    [Theory]
    [InlineData("escaped\\", "123", "")]
    [InlineData("caf\u00e9", "123", "")]
    public void MirrorPromotionGate_character_outside_the_ascii_word_class_before_the_reference_is_blocked_by_the_dead_ref_sweep(string prefix, string number, string suffix)
    {
        var line = BareReferenceExample(prefix, number, suffix);

        IsBlockedByDeadRefSweep(line).Should().BeTrue();
    }
}

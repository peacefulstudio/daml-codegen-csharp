// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.RegularExpressions;
using AwesomeAssertions;
using Xunit;

namespace Daml.Codegen.CSharp.Tests;

public class LegacyDecoderSuppressionIsConfinedToTestsTests
{
    [Fact]
    public void SourceTree_should_contain_no_DAMLRT0001_or_CS0618_suppressions()
    {
        var offenders = SourceFiles().SelectMany(Suppressions).ToList();

        offenders.Should().BeEmpty(
            "src/ ships the obsolete DAMLRT0001 entry points precisely so callers keep seeing the "
            + "warning; a pragma suppressing it (or the underlying CS0618) there would hide a real "
            + "call site instead of a deliberately-pinning test");
    }

    private static readonly Regex SuppressedDiagnostic =
        new("""^\s*#pragma\s+warning\s+disable.*\b(DAMLRT0001|CS0618)\b""", RegexOptions.Multiline);

    private static IEnumerable<string> SourceFiles() =>
        Directory.EnumerateFiles(Path.Combine(RepositoryRoot(), "src"), "*.cs", SearchOption.AllDirectories);

    private static IEnumerable<string> Suppressions(string file) =>
        SuppressedDiagnostic.Matches(File.ReadAllText(file)).Select(match => $"{file}: {match.Value.Trim()}");

    private static string RepositoryRoot()
    {
        for (var candidate = new DirectoryInfo(AppContext.BaseDirectory);
             candidate is not null;
             candidate = candidate.Parent)
        {
            var hasSourceTree = Directory.Exists(Path.Combine(candidate.FullName, "src"));
            var hasGitEntry = Directory.Exists(Path.Combine(candidate.FullName, ".git"))
                || File.Exists(Path.Combine(candidate.FullName, ".git"));
            if (hasSourceTree && hasGitEntry)
            {
                return candidate.FullName;
            }
        }

        throw new DirectoryNotFoundException(
            $"No ancestor of '{AppContext.BaseDirectory}' contains both a 'src' directory and a "
            + "'.git' entry; cannot locate the repository root.");
    }
}

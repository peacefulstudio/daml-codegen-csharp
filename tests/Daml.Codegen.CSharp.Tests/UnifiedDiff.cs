// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text;

namespace Daml.Codegen.CSharp.Tests;

/// <summary>
/// Produces a unified-diff-style string from two pieces of UTF-8 text.
/// Used by <see cref="DriftDetectionTests"/> to turn opaque byte-equality
/// failures into a readable, line-level diff in the test output.
/// </summary>
internal static class UnifiedDiff
{
    private const int ContextLines = 3;
    private const int MaxHunks = 10;
    private static readonly string[] LineSeparators = ["\r\n", "\r", "\n"];

    /// <summary>
    /// Returns a unified-diff string comparing <paramref name="expectedText"/>
    /// and <paramref name="actualText"/>, or <see langword="null"/> when both
    /// inputs are identical.  At most <c>10</c> hunks are emitted so large
    /// diffs remain readable in test output.
    /// </summary>
    internal static string? Render(string expectedText, string actualText)
    {
        var expected = SplitLines(expectedText);
        var actual = SplitLines(actualText);

        var edits = ComputeEdits(expected, actual);
        if (edits.Count == 0)
            return null;

        return FormatHunks(expected, actual, edits);
    }

    /// <summary>
    /// Decodes <paramref name="expectedBytes"/> and <paramref name="actualBytes"/>
    /// as UTF-8 and delegates to <see cref="Render(string,string)"/>.
    /// </summary>
    internal static string? Render(byte[] expectedBytes, byte[] actualBytes) =>
        Render(
            Encoding.UTF8.GetString(expectedBytes),
            Encoding.UTF8.GetString(actualBytes));

    private static string[] SplitLines(string text) =>
        text.Split(LineSeparators, StringSplitOptions.None);

    private sealed record Edit(EditKind Kind, int ExpectedIndex, int ActualIndex);

    private enum EditKind { Delete, Insert }

    private static List<Edit> ComputeEdits(string[] expected, string[] actual)
    {
        var lcs = BuildLcsTable(expected, actual);
        var edits = new List<Edit>();
        CollectEdits(lcs, expected, actual, expected.Length, actual.Length, edits);
        return edits;
    }

    private static int[,] BuildLcsTable(string[] a, string[] b)
    {
        var m = a.Length;
        var n = b.Length;
        var dp = new int[m + 1, n + 1];
        for (var i = 1; i <= m; i++)
        for (var j = 1; j <= n; j++)
            dp[i, j] = a[i - 1] == b[j - 1]
                ? dp[i - 1, j - 1] + 1
                : Math.Max(dp[i - 1, j], dp[i, j - 1]);
        return dp;
    }

    private static void CollectEdits(
        int[,] lcs,
        string[] a,
        string[] b,
        int i,
        int j,
        List<Edit> edits)
    {
        while (true)
        {
            if (i > 0 && j > 0 && a[i - 1] == b[j - 1])
            {
                i--;
                j--;
            }
            else if (j > 0 && (i == 0 || lcs[i, j - 1] >= lcs[i - 1, j]))
            {
                edits.Add(new Edit(EditKind.Insert, i, j - 1));
                j--;
            }
            else if (i > 0 && (j == 0 || lcs[i, j - 1] < lcs[i - 1, j]))
            {
                edits.Add(new Edit(EditKind.Delete, i - 1, j));
                i--;
            }
            else
            {
                break;
            }
        }

        edits.Reverse();
    }

    private static string FormatHunks(string[] expected, string[] actual, List<Edit> edits)
    {
        var hunks = GroupIntoHunks(edits);
        var sb = new StringBuilder();

        var emitted = 0;
        foreach (var hunk in hunks)
        {
            if (emitted >= MaxHunks)
            {
                sb.AppendLine($"... (truncated after {MaxHunks} hunks)");
                break;
            }

            var expStart = Math.Max(0, hunk[0].ExpectedIndex - ContextLines);
            var expEnd = Math.Min(expected.Length - 1, hunk[^1].ExpectedIndex + ContextLines);
            var actStart = Math.Max(0, hunk[0].ActualIndex - ContextLines);
            var actEnd = Math.Min(actual.Length - 1, hunk[^1].ActualIndex + ContextLines);

            sb.AppendLine($"@@ -{expStart + 1},{expEnd - expStart + 1} +{actStart + 1},{actEnd - actStart + 1} @@");

            var editIdx = 0;
            var expLine = expStart;
            var actLine = actStart;

            while (expLine <= expEnd || actLine <= actEnd)
            {
                var edit = editIdx < hunk.Count ? hunk[editIdx] : null;

                if (edit != null && edit.Kind == EditKind.Delete && edit.ExpectedIndex == expLine)
                {
                    sb.AppendLine($"-{expected[expLine]}");
                    expLine++;
                    editIdx++;
                }
                else if (edit != null && edit.Kind == EditKind.Insert && edit.ActualIndex == actLine)
                {
                    sb.AppendLine($"+{actual[actLine]}");
                    actLine++;
                    editIdx++;
                }
                else if (expLine <= expEnd && actLine <= actEnd)
                {
                    sb.AppendLine($" {expected[expLine]}");
                    expLine++;
                    actLine++;
                }
                else
                {
                    break;
                }
            }

            emitted++;
        }

        return sb.ToString();
    }

    private static List<List<Edit>> GroupIntoHunks(List<Edit> edits)
    {
        var hunks = new List<List<Edit>>();
        var current = new List<Edit>();

        foreach (var edit in edits)
        {
            if (current.Count > 0)
            {
                var last = current[^1];
                var gap = edit.Kind == EditKind.Delete
                    ? edit.ExpectedIndex - last.ExpectedIndex
                    : edit.ActualIndex - last.ActualIndex;

                if (gap > ContextLines * 2 + 1)
                {
                    hunks.Add(current);
                    current = new List<Edit>();
                }
            }

            current.Add(edit);
        }

        if (current.Count > 0)
            hunks.Add(current);

        return hunks;
    }
}

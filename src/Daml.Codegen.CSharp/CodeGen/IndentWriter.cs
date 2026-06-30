// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text;

namespace Daml.Codegen.CSharp.CodeGen;

/// <summary>
/// Helper for writing indented code.
/// </summary>
internal sealed class IndentWriter(StringBuilder sb)
{
    private int _indentLevel;
    private const string IndentString = "    ";

    private readonly SortedSet<string> _requiredUsings = new(StringComparer.Ordinal);

    public string CurrentTypeName { get; set; } = "";

    /// <summary>Records that the given namespace is referenced in this file.</summary>
    public void Require(string ns) => _requiredUsings.Add(ns);

    /// <summary>Returns the sorted set of namespaces required by this file.</summary>
    public IReadOnlyCollection<string> RequiredUsings => _requiredUsings;

    private bool _atLineStart = true;

    public void Indent() => _indentLevel++;
    public void Dedent() => _indentLevel = Math.Max(0, _indentLevel - 1);

    public void Append(string text)
    {
        WriteIndentIfAtLineStart();
        sb.Append(text);
    }

    private void WriteIndentIfAtLineStart()
    {
        if (!_atLineStart)
        {
            return;
        }
        for (int i = 0; i < _indentLevel; i++)
        {
            sb.Append(IndentString);
        }
        _atLineStart = false;
    }

    // Emit LF explicitly rather than via `StringBuilder.AppendLine` (which uses
    // `Environment.NewLine`). Generated source is published in NuGet packages
    // and compared by `DriftDetectionTests` byte-for-byte; OS-dependent line
    // endings would make a Windows codegen run produce different bytes from a
    // macOS/Linux one for no behaviour change. LF is the conventional choice
    // for cross-platform source distribution and matches what `.editorconfig`
    // pins repo-wide.
    private const char Newline = '\n';

    public void AppendLine(string? line = null)
    {
        if (line is not null)
        {
            WriteIndentIfAtLineStart();
            sb.Append(line);
        }
        sb.Append(Newline);
        _atLineStart = true;
    }
}
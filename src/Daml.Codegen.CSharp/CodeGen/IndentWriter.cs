using System.Text;

namespace Daml.Codegen.CSharp.CodeGen;

/// <summary>
/// Helper for writing indented code.
/// </summary>
internal sealed class IndentWriter(StringBuilder sb)
{
    private int _indentLevel;
    private const string IndentString = "    ";

    public string CurrentTypeName { get; set; } = "";

    public void Indent() => _indentLevel++;
    public void Dedent() => _indentLevel = Math.Max(0, _indentLevel - 1);

    public void Append(string text) => sb.Append(text);

    public void AppendLine(string? line = null)
    {
        if (line is not null)
        {
            for (int i = 0; i < _indentLevel; i++)
            {
                sb.Append(IndentString);
            }
            sb.AppendLine(line);
        }
        else
        {
            sb.AppendLine();
        }
    }
}
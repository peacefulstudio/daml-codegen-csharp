// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using System.Text.RegularExpressions;

namespace Daml.Codegen.CSharp.CodeGen;

/// <summary>
/// C# identifier sanitisation and casing shared by the emitter and its per-package
/// scan. Pure functions over a Daml name: escape invalid characters, avoid
/// leading-digit and keyword collisions, and PascalCase segment-delimited names.
/// </summary>
internal static partial class Identifiers
{
    private const string FallbackNamespace = "DamlGenerated";

    /// <summary>
    /// Derives a C# namespace from a Daml package name: PascalCases each
    /// <c>-</c>/<c>_</c>-delimited segment and joins with <c>.</c>, falling back to
    /// <c>DamlGenerated</c> when no usable segment remains.
    /// </summary>
    public static string DeriveNamespace(string packageName)
    {
        var parts = packageName.Split('-', '_')
            .Select(ToPascalCase)
            .Select(Sanitize)
            .Where(segment => segment.Length > 0)
            .ToList();
        return parts.Count == 0 ? FallbackNamespace : string.Join(".", parts);
    }

    /// <summary>
    /// Replaces characters invalid in a C# identifier with <c>_</c>, prefixes a
    /// leading digit with <c>_</c>, and escapes C# keywords with <c>@</c>.
    /// </summary>
    public static string Sanitize(string name)
    {
        var sanitized = IdentifierRegex().Replace(name, "_");

        if (sanitized.Length == 0)
        {
            return sanitized;
        }

        if (char.IsDigit(sanitized[0]))
        {
            sanitized = "_" + sanitized;
        }

        if (CSharpKeywords.Contains(sanitized))
        {
            sanitized = "@" + sanitized;
        }

        return sanitized;
    }

    /// <summary>
    /// PascalCases a name across <c>_</c>, <c>-</c> and <c>.</c> delimiters,
    /// prefixing a leading-digit result with <c>_</c>.
    /// </summary>
    public static string ToPascalCase(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return name;
        }

        var sb = new StringBuilder();
        var capitalizeNext = true;

        foreach (var c in name)
        {
            if (c is '_' or '-' or '.')
            {
                capitalizeNext = true;
            }
            else if (capitalizeNext)
            {
                sb.Append(char.ToUpperInvariant(c));
                capitalizeNext = false;
            }
            else
            {
                sb.Append(c);
            }
        }

        var result = sb.ToString();

        if (result.Length > 0 && char.IsDigit(result[0]))
        {
            return "_" + result;
        }

        return result;
    }

    /// <summary>
    /// Derives the C# member identifier for a Daml field, PascalCasing and
    /// sanitising the Daml name and disambiguating with a trailing <c>_</c> when
    /// the result would equal <paramref name="enclosingTypeName"/> (illegal in
    /// C#: CS0542 member names cannot be the same as their enclosing type). The
    /// Daml wire name is unaffected — only the emitted C# identifier changes.
    /// </summary>
    internal static string MemberName(string damlFieldName, string enclosingTypeName) =>
        Disambiguate(ToPascalCase(Sanitize(damlFieldName)), enclosingTypeName);

    /// <summary>
    /// Builds the C# marker-interface name for a Daml interface: the sanitised
    /// interface name prefixed with <c>I</c> (e.g. Daml <c>Holding</c> →
    /// <c>IHolding</c>). Shared by the interface emitter and the type resolver so a
    /// reference to an interface names the same marker on the field-type path as on
    /// the choice-exercise path.
    /// </summary>
    internal static string InterfaceMarkerName(string interfaceName) => "I" + Sanitize(interfaceName);

    /// <summary>
    /// Appends a trailing <c>_</c> when <paramref name="identifier"/> equals
    /// <paramref name="enclosingTypeName"/>, which is illegal in C# (CS0542: member
    /// names cannot be the same as their enclosing type).
    /// </summary>
    internal static string Disambiguate(string identifier, string enclosingTypeName) =>
        identifier == enclosingTypeName ? identifier + "_" : identifier;

    [GeneratedRegex("[^a-zA-Z0-9_]")]
    private static partial Regex IdentifierRegex();

    private static readonly HashSet<string> CSharpKeywords =
    [
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char",
        "checked", "class", "const", "continue", "decimal", "default", "delegate",
        "do", "double", "else", "enum", "event", "explicit", "extern", "false",
        "finally", "fixed", "float", "for", "foreach", "goto", "if", "implicit",
        "in", "int", "interface", "internal", "is", "lock", "long", "namespace",
        "new", "null", "object", "operator", "out", "override", "params", "private",
        "protected", "public", "readonly", "ref", "return", "sbyte", "sealed",
        "short", "sizeof", "stackalloc", "static", "string", "struct", "switch",
        "this", "throw", "true", "try", "typeof", "uint", "ulong", "unchecked",
        "unsafe", "ushort", "using", "virtual", "void", "volatile", "while"
    ];
}

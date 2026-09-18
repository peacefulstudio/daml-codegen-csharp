// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Daml.Runtime.Serialization;

/// <summary>
/// Carries the ambient state a <see cref="DamlLfJsonDecoders"/> entry point needs to decode one
/// LF-JSON value: the shared decode limits, the current recursion depth, and a JSON-pointer-like
/// path used to locate the value in error messages.
/// </summary>
/// <remarks>
/// <para>
/// A generated decoder builds one context at the root via <see cref="Root"/> and threads a nested
/// context — obtained from <see cref="Field"/> or <see cref="Element"/> — into each composite
/// reader it calls, exactly as <see cref="DamlLfJsonDecoders"/>' own composite entry points thread
/// context into the element readers they are given.
/// </para>
/// <para>
/// Because C# always synthesizes a public parameterless constructor for a struct, a caller who
/// writes <c>default(DamlLfJsonDecodeContext)</c> or <c>new DamlLfJsonDecodeContext()</c> bypasses
/// <see cref="Root"/> and its validation. Rather than let that ambient state misbehave, <see cref="Limits"/>
/// and <see cref="Path"/> are computed: an uninitialized context — one whose backing state was never
/// set by <see cref="Root"/> — self-heals to the same defaults <see cref="Root"/> applies when its own
/// arguments are omitted, so it behaves exactly like <c>Root("$")</c>. A context built by <see cref="Root"/>
/// reports its stored limits and path as-is, including an explicit zero limit — zero is an ordinary,
/// validatable limit value here, not a "use the default" sentinel.
/// </para>
/// </remarks>
public readonly record struct DamlLfJsonDecodeContext
{
    private const string RootPath = "$";

    private readonly DamlJsonDeserializationLimits _limits;
    private readonly string? _path;
    private readonly bool _isRooted;

    private DamlLfJsonDecodeContext(DamlJsonDeserializationLimits limits, int depth, string path, bool isRooted)
    {
        _limits = limits;
        Depth = depth;
        _path = path;
        _isRooted = isRooted;
    }

    /// <summary>
    /// The decode limits in effect for this decode. A context obtained via <c>default</c> or
    /// <c>new()</c> rather than <see cref="Root"/> reports <see cref="DamlJsonSerializer.DefaultDeserializationLimits"/>;
    /// a context built by <see cref="Root"/> reports the limits it was given, unchanged, including an explicit zero.
    /// </summary>
    public DamlJsonDeserializationLimits Limits => _isRooted ? _limits : DamlJsonSerializer.DefaultDeserializationLimits;

    /// <summary>The recursion depth of the value this context describes, zero at the root.</summary>
    public int Depth { get; }

    /// <summary>
    /// A path locating the value this context describes, for use in error messages. A context
    /// obtained via <c>default</c> or <c>new()</c> rather than <see cref="Root"/> reports the
    /// conventional root path <c>"$"</c>, matching the path <see cref="System.Text.Json.JsonException.Path"/>
    /// reports for a document root.
    /// </summary>
    public string Path => _path ?? RootPath;

    /// <summary>
    /// Creates the root context for a decode rooted at <paramref name="path"/>.
    /// </summary>
    /// <param name="path">The path to report for the root value, typically the decoded type's name.</param>
    /// <param name="limits">Decode limits; the shared hardened defaults apply when omitted. A zero limit is
    /// accepted and enforced as given — it is not treated as "use the default".</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="limits"/> is not a valid limit configuration.</exception>
    public static DamlLfJsonDecodeContext Root(string path, DamlJsonDeserializationLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(path);
        var effectiveLimits = limits ?? DamlJsonSerializer.DefaultDeserializationLimits;
        DamlJsonSerializer.ValidateLimits(effectiveLimits);
        return new DamlLfJsonDecodeContext(effectiveLimits, depth: 0, path, isRooted: true);
    }

    /// <summary>
    /// A context for the named field of the record this context describes.
    /// </summary>
    /// <exception cref="System.Text.Json.JsonException">The nested value would exceed the maximum supported decode depth.</exception>
    public DamlLfJsonDecodeContext Field(string label) =>
        Nested($"{Path}.{label}");

    /// <summary>
    /// A context for the element at <paramref name="index"/> of the list or array this context describes.
    /// </summary>
    /// <exception cref="System.Text.Json.JsonException">The nested value would exceed the maximum supported decode depth.</exception>
    public DamlLfJsonDecodeContext Element(int index) =>
        Nested($"{Path}[{index}]");

    internal DamlLfJsonDecodeContext Nested(string path)
    {
        var depth = Depth + 1;
        if (depth > DamlJsonSerializer.MaximumNestingDepth)
        {
            throw DamlJsonSerializer.DepthBoundExceeded();
        }
        return new DamlLfJsonDecodeContext(_limits, depth, path, _isRooted);
    }
}

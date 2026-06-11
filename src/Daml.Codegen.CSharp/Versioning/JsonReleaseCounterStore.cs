// Copyright (c) 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Daml.Codegen.CSharp.Versioning;

/// <summary>
/// File-backed store of emitter-counter values keyed by
/// <c>{packageName}@{Major.Minor.Patch}</c>. The 4th NuGet version segment
/// is derived from this store: <c>0</c> for any first emission of a
/// (package, intrinsic) pair, incremented only when the same pair is
/// re-emitted with a different content hash.
/// </summary>
/// <remarks>
/// <para><b>Single-writer precondition.</b> Instances are not thread-safe and
/// the on-disk file uses no cross-process locking. Callers must serialize
/// access to a given store path — both across threads in one process and
/// across processes. The release pipeline that owns the store path satisfies
/// this naturally: it runs as a single job, sequentially per package. If concurrent writers were ever allowed against the same path,
/// the last-writer-wins truncating write would silently drop a revision
/// bump and two distinct content hashes could end up sharing the same
/// 4th-segment value.</para>
/// <para><b>Atomic on-disk update.</b> Each <see cref="ResolveRevision"/>
/// write goes via a sibling <c>.tmp</c> file and an atomic
/// <see cref="File.Move(string, string, bool)"/>, so a crash mid-write
/// leaves the previous valid file intact rather than truncating it to
/// empty.</para>
/// </remarks>
internal sealed class JsonReleaseCounterStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _path;
    private readonly Dictionary<string, ReleaseCounterEntry> _entries;

    private JsonReleaseCounterStore(string path, Dictionary<string, ReleaseCounterEntry> entries)
    {
        _path = path;
        _entries = entries;
    }

    /// <summary>
    /// Opens an existing JSON store at <paramref name="path"/>, or returns an
    /// empty in-memory store that will be persisted on the first
    /// <see cref="ResolveRevision"/> call if the file does not yet exist.
    /// </summary>
    /// <exception cref="InvalidDataException">
    /// Thrown when the file exists but does not parse as the expected JSON
    /// shape (truncated, hand-edited, merge-conflict markers). The exception
    /// names the offending path and JSON position so the failure is
    /// diagnosable mid-CI-run. Recovery is a human decision (silently
    /// rebuilding from empty would re-zero every recorded revision and break
    /// monotonicity), so this never falls back to an empty store on parse
    /// failure.
    /// </exception>
    public static JsonReleaseCounterStore OpenOrCreate(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
        {
            return new JsonReleaseCounterStore(path, new Dictionary<string, ReleaseCounterEntry>(StringComparer.Ordinal));
        }

        var json = File.ReadAllText(path);
        if (string.IsNullOrWhiteSpace(json))
        {
            return new JsonReleaseCounterStore(path, new Dictionary<string, ReleaseCounterEntry>(StringComparer.Ordinal));
        }

        Dictionary<string, ReleaseCounterEntry?>? loaded;
        try
        {
            loaded = JsonSerializer.Deserialize<Dictionary<string, ReleaseCounterEntry?>>(json, SerializerOptions);
        }
        catch (JsonException inner)
        {
            throw new InvalidDataException(
                $"Release-counter store at '{path}' is not valid JSON (line {inner.LineNumber}, position {inner.BytePositionInLine}). Repair the file or delete it to start from an empty counter table.",
                inner);
        }

        var validated = new Dictionary<string, ReleaseCounterEntry>(StringComparer.Ordinal);
        if (loaded is null) return new JsonReleaseCounterStore(path, validated);

        foreach (var (key, entry) in loaded)
        {
            if (entry is null)
            {
                throw new InvalidDataException(
                    $"Release-counter store at '{path}' has a null entry for key '{key}'. Repair the file or delete it to start from an empty counter table.");
            }

            if (string.IsNullOrEmpty(entry.ContentHash))
            {
                throw new InvalidDataException(
                    $"Release-counter store at '{path}' has a missing or empty content_hash for key '{key}'. Repair the file or delete it to start from an empty counter table.");
            }

            if (entry.Revision < 0)
            {
                throw new InvalidDataException(
                    $"Release-counter store at '{path}' has a negative revision ({entry.Revision}) for key '{key}'. Repair the file or delete it to start from an empty counter table.");
            }

            validated[key] = entry;
        }

        return new JsonReleaseCounterStore(path, validated);
    }

    /// <summary>
    /// Resolves and persists the 4th-segment revision for a given
    /// (<paramref name="packageName"/>, <paramref name="intrinsicVersion"/>,
    /// <paramref name="contentHash"/>) tuple. Semantics:
    /// <list type="bullet">
    /// <item>Unknown key → write hash@0, return <c>0</c>.</item>
    /// <item>Known key + identical hash → return the recorded revision unchanged.</item>
    /// <item>Known key + differing hash → bump revision, write new hash@(r+1), return new revision.</item>
    /// </list>
    /// </summary>
    public int ResolveRevision(string packageName, Version intrinsicVersion, string contentHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageName);
        ArgumentNullException.ThrowIfNull(intrinsicVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentHash);

        var key = ComposeKey(packageName, intrinsicVersion);

        if (_entries.TryGetValue(key, out var existing))
        {
            if (string.Equals(existing.ContentHash, contentHash, StringComparison.Ordinal))
            {
                return existing.Revision;
            }

            var bumped = existing.Revision + 1;
            _entries[key] = new ReleaseCounterEntry(contentHash, bumped);
            Persist();
            return bumped;
        }

        _entries[key] = new ReleaseCounterEntry(contentHash, 0);
        Persist();
        return 0;
    }

    private static string ComposeKey(string packageName, Version intrinsic) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{packageName}@{intrinsic.Major}.{intrinsic.Minor}.{Math.Max(0, intrinsic.Build)}");

    private void Persist()
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var ordered = new SortedDictionary<string, ReleaseCounterEntry>(_entries, StringComparer.Ordinal);
        var json = JsonSerializer.Serialize(ordered, SerializerOptions);
        var temporaryPath = _path + ".tmp";
        File.WriteAllText(temporaryPath, json);
        File.Move(temporaryPath, _path, overwrite: true);
    }
}

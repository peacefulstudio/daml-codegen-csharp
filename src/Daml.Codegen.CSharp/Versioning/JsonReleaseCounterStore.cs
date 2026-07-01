// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;

namespace Daml.Codegen.CSharp.Versioning;

/// <summary>
/// File-backed store of a single per-source "codegen-generation" ordinal:
/// the monotonic 4th NuGet version segment shared by every package emitted
/// while a given codegen-tool version is current. Keyed by the codegen-tool
/// version string (<see cref="EmitterVersion.Current"/>), not by package —
/// every package produced during one run of the same codegen version
/// resolves to the identical ordinal by construction.
/// </summary>
/// <remarks>
/// <para><b>Single-writer precondition.</b> Instances are not thread-safe and
/// the on-disk file uses no cross-process locking. Callers must serialize
/// access to a given store path — both across threads in one process and
/// across processes. The release pipeline that owns the store path satisfies
/// this naturally: it runs as a single job, sequentially per package.</para>
/// <para><b>Atomic on-disk update.</b> Each minted ordinal is written via a
/// sibling <c>.tmp</c> file and an atomic <see cref="File.Move(string, string, bool)"/>,
/// so a crash mid-write leaves the previous valid file intact rather than
/// truncating it to empty.</para>
/// <para><b>Shape.</b> Persisted as <c>{ "codegen_generations": { "&lt;codegenVersion&gt;": &lt;ordinal:int&gt; } }</c>.</para>
/// <para><b>Legacy migration.</b> A store file lacking a top-level
/// <c>codegen_generations</c> property is assumed to be in the retired
/// per-package shape (<c>{ "&lt;packageName&gt;@&lt;M.m.p&gt;": { "content_hash": ..., "revision": &lt;int&gt; } }</c>).
/// When detected, its entries are not carried forward; instead the highest
/// <c>revision</c> found across them becomes the floor that the first
/// newly-minted ordinal must exceed, so a freshly-introduced codegen version
/// can never collide with an already-published per-package revision.</para>
/// </remarks>
internal sealed class JsonReleaseCounterStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private sealed record StoreDocument(SortedDictionary<string, int> CodegenGenerations);

    private readonly string _path;
    private readonly SortedDictionary<string, int> _ordinalsByCodegenVersion;
    private readonly int _migrationFloor;

    private JsonReleaseCounterStore(string path, SortedDictionary<string, int> ordinalsByCodegenVersion, int migrationFloor)
    {
        _path = path;
        _ordinalsByCodegenVersion = ordinalsByCodegenVersion;
        _migrationFloor = migrationFloor;
    }

    /// <summary>
    /// Opens an existing JSON store at <paramref name="path"/>, or returns an
    /// empty in-memory store that will be persisted on the first
    /// <see cref="ResolveGeneration"/> call if the file does not yet exist.
    /// Detects the file's shape by the presence of a top-level
    /// <c>codegen_generations</c> property, and migrates a legacy per-package
    /// file's highest revision into the new store's floor (see remarks on
    /// <see cref="JsonReleaseCounterStore"/>).
    /// </summary>
    /// <exception cref="InvalidDataException">
    /// Thrown when the file exists but does not parse as valid JSON or
    /// contains a structurally invalid entry (truncated, hand-edited,
    /// merge-conflict markers). The exception names the offending path so the
    /// failure is diagnosable mid-CI-run. Recovery is a human decision
    /// (silently rebuilding from empty would re-zero the generation counter
    /// and break monotonicity), so this never falls back to an empty store on
    /// parse failure.
    /// </exception>
    public static JsonReleaseCounterStore OpenOrCreate(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
        {
            return new JsonReleaseCounterStore(path, new SortedDictionary<string, int>(StringComparer.Ordinal), -1);
        }

        var json = File.ReadAllText(path);
        if (string.IsNullOrWhiteSpace(json))
        {
            return new JsonReleaseCounterStore(path, new SortedDictionary<string, int>(StringComparer.Ordinal), -1);
        }

        Dictionary<string, JsonElement>? loaded;
        try
        {
            loaded = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json, SerializerOptions);
        }
        catch (JsonException inner)
        {
            throw new InvalidDataException(
                $"Release-counter store at '{path}' is not valid JSON (line {inner.LineNumber}, position {inner.BytePositionInLine}). Repair the file or delete it to start from an empty counter table.",
                inner);
        }

        if (loaded is null || loaded.Count == 0)
        {
            return new JsonReleaseCounterStore(path, new SortedDictionary<string, int>(StringComparer.Ordinal), -1);
        }

        if (loaded.TryGetValue("codegen_generations", out var generationsElement))
        {
            if (generationsElement.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException(
                    $"Release-counter store at '{path}' has a non-object 'codegen_generations' value. Repair the file or delete it to start from an empty counter table.");
            }

            if (loaded.Count != 1)
            {
                throw new InvalidDataException(
                    $"Release-counter store at '{path}' mixes a top-level 'codegen_generations' property with other " +
                    "unexpected top-level keys. Repair the file or delete it to start from an empty counter table.");
            }

            var ordinals = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach (var property in generationsElement.EnumerateObject())
            {
                ordinals[property.Name] = property.Value.GetInt32();
            }
            return new JsonReleaseCounterStore(path, ordinals, -1);
        }

        var legacyFloor = -1;
        foreach (var (key, entry) in loaded)
        {
            if (entry.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException(
                    $"Release-counter store at '{path}' has a null or invalid entry for key '{key}'. Repair the file or delete it to start from an empty counter table.");
            }

            if (!entry.TryGetProperty("content_hash", out var contentHashProperty)
                || contentHashProperty.ValueKind != JsonValueKind.String
                || string.IsNullOrEmpty(contentHashProperty.GetString()))
            {
                throw new InvalidDataException(
                    $"Release-counter store at '{path}' has a missing or empty content_hash for key '{key}'. Repair the file or delete it to start from an empty counter table.");
            }

            if (!entry.TryGetProperty("revision", out var revisionProperty) || revisionProperty.ValueKind != JsonValueKind.Number)
            {
                throw new InvalidDataException(
                    $"Release-counter store at '{path}' has a missing or non-numeric revision for key '{key}'. Repair the file or delete it to start from an empty counter table.");
            }

            var revision = revisionProperty.GetInt32();
            if (revision < 0)
            {
                throw new InvalidDataException(
                    $"Release-counter store at '{path}' has a negative revision ({revision}) for key '{key}'. Repair the file or delete it to start from an empty counter table.");
            }

            legacyFloor = Math.Max(legacyFloor, revision);
        }

        return new JsonReleaseCounterStore(path, new SortedDictionary<string, int>(StringComparer.Ordinal), legacyFloor);
    }

    /// <summary>
    /// Resolves and persists the shared per-source generation ordinal for
    /// <paramref name="codegenVersion"/>. Semantics:
    /// <list type="bullet">
    /// <item>Already-seen <paramref name="codegenVersion"/> → return the
    /// stored ordinal unchanged, with no write. Idempotent across every
    /// package resolved in the same run.</item>
    /// <item>Never-seen <paramref name="codegenVersion"/> → mint
    /// <c>(highest ordinal currently known to the store, including any
    /// migrated legacy floor) + 1</c>, persist it, and return it.</item>
    /// </list>
    /// </summary>
    public int ResolveGeneration(string codegenVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(codegenVersion);

        if (_ordinalsByCodegenVersion.TryGetValue(codegenVersion, out var existing))
        {
            return existing;
        }

        var highestKnown = _ordinalsByCodegenVersion.Count == 0
            ? _migrationFloor
            : Math.Max(_migrationFloor, _ordinalsByCodegenVersion.Values.Max());

        var minted = highestKnown + 1;
        _ordinalsByCodegenVersion[codegenVersion] = minted;
        Persist();
        return minted;
    }

    private void Persist()
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(new StoreDocument(_ordinalsByCodegenVersion), SerializerOptions);
        var temporaryPath = _path + ".tmp";
        File.WriteAllText(temporaryPath, json);
        File.Move(temporaryPath, _path, overwrite: true);
    }
}

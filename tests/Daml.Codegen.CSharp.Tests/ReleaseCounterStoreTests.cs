// Copyright (c) 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using Daml.Codegen.CSharp.Versioning;
using FluentAssertions;
using Xunit;

namespace Daml.Codegen.CSharp.Tests;

public class ReleaseCounterStoreTests : IDisposable
{
    private readonly string _storePath;

    public ReleaseCounterStoreTests()
    {
        _storePath = Path.Combine(
            Path.GetTempPath(),
            $"release-counters-{Guid.NewGuid():N}.json");
    }

    public void Dispose()
    {
        if (File.Exists(_storePath)) File.Delete(_storePath);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void ResolveRevision_returns_zero_and_persists_entry_for_unknown_pair()
    {
        var store = JsonReleaseCounterStore.OpenOrCreate(_storePath);

        var revision = store.ResolveRevision(
            packageName: "Splice.Amulet",
            intrinsicVersion: new Version(0, 1, 17),
            contentHash: "abc123");

        revision.Should().Be(0);

        var reopened = JsonReleaseCounterStore.OpenOrCreate(_storePath);
        reopened.ResolveRevision("Splice.Amulet", new Version(0, 1, 17), "abc123")
            .Should().Be(0);
    }

    [Fact]
    public void ResolveRevision_holds_revision_steady_when_content_hash_matches()
    {
        var store = JsonReleaseCounterStore.OpenOrCreate(_storePath);
        store.ResolveRevision("Splice.Amulet", new Version(0, 1, 17), "hash-a").Should().Be(0);

        store.ResolveRevision("Splice.Amulet", new Version(0, 1, 17), "hash-a").Should().Be(0);
        store.ResolveRevision("Splice.Amulet", new Version(0, 1, 17), "hash-a").Should().Be(0);
    }

    [Fact]
    public void ResolveRevision_persists_bumps_across_OpenOrCreate_reopens()
    {
        JsonReleaseCounterStore.OpenOrCreate(_storePath)
            .ResolveRevision("Splice.Amulet", new Version(0, 1, 17), "hash-a")
            .Should().Be(0);
        JsonReleaseCounterStore.OpenOrCreate(_storePath)
            .ResolveRevision("Splice.Amulet", new Version(0, 1, 17), "hash-b")
            .Should().Be(1);

        JsonReleaseCounterStore.OpenOrCreate(_storePath)
            .ResolveRevision("Splice.Amulet", new Version(0, 1, 17), "hash-b")
            .Should().Be(1);
    }

    [Fact]
    public void ResolveRevision_tracks_each_packageName_and_intrinsic_version_independently()
    {
        var store = JsonReleaseCounterStore.OpenOrCreate(_storePath);
        store.ResolveRevision("Splice.Amulet", new Version(0, 1, 17), "amulet-hash").Should().Be(0);
        store.ResolveRevision("Splice.Util", new Version(0, 1, 5), "util-hash").Should().Be(0);
        store.ResolveRevision("Splice.Amulet", new Version(0, 1, 18), "amulet-next").Should().Be(0);

        store.ResolveRevision("Splice.Amulet", new Version(0, 1, 17), "amulet-rebuilt").Should().Be(1);

        store.ResolveRevision("Splice.Util", new Version(0, 1, 5), "util-hash").Should().Be(0);
        store.ResolveRevision("Splice.Amulet", new Version(0, 1, 18), "amulet-next").Should().Be(0);
    }

    [Fact]
    public void OpenOrCreate_throws_InvalidDataException_when_an_entry_value_is_null()
    {
        File.WriteAllText(_storePath, "{ \"Splice.Amulet@0.1.17\": null }");

        var action = () => JsonReleaseCounterStore.OpenOrCreate(_storePath);

        action.Should().Throw<InvalidDataException>()
            .Which.Message.Should().Contain("Splice.Amulet@0.1.17");
    }

    [Fact]
    public void OpenOrCreate_throws_InvalidDataException_when_an_entry_revision_is_negative()
    {
        File.WriteAllText(_storePath, "{ \"Splice.Amulet@0.1.17\": { \"content_hash\": \"abc\", \"revision\": -5 } }");

        var action = () => JsonReleaseCounterStore.OpenOrCreate(_storePath);

        action.Should().Throw<InvalidDataException>()
            .Which.Message.Should().Contain("Splice.Amulet@0.1.17");
    }

    [Fact]
    public void OpenOrCreate_throws_InvalidDataException_when_an_entry_content_hash_is_null()
    {
        File.WriteAllText(_storePath, "{ \"Splice.Amulet@0.1.17\": { \"content_hash\": null, \"revision\": 1 } }");

        var action = () => JsonReleaseCounterStore.OpenOrCreate(_storePath);

        action.Should().Throw<InvalidDataException>()
            .Which.Message.Should().Contain("Splice.Amulet@0.1.17");
    }

    [Fact]
    public void OpenOrCreate_throws_InvalidDataException_naming_the_path_when_file_contains_malformed_json()
    {
        File.WriteAllText(_storePath, "{ this is not valid json");

        var action = () => JsonReleaseCounterStore.OpenOrCreate(_storePath);

        action.Should().Throw<InvalidDataException>()
            .Which.Message.Should().Contain(_storePath);
    }

    [Fact]
    public void Persist_does_not_leave_a_dot_tmp_sibling_after_a_successful_write()
    {
        var store = JsonReleaseCounterStore.OpenOrCreate(_storePath);
        store.ResolveRevision("Splice.Amulet", new Version(0, 1, 17), "deadbeef").Should().Be(0);

        File.Exists(_storePath).Should().BeTrue();
        File.Exists(_storePath + ".tmp").Should().BeFalse();
    }

    [Fact]
    public void Persist_writes_packageName_at_intrinsicVersion_key_with_snake_case_entry_fields()
    {
        var store = JsonReleaseCounterStore.OpenOrCreate(_storePath);
        store.ResolveRevision("Splice.Amulet", new Version(0, 1, 17), "deadbeef").Should().Be(0);

        using var document = JsonDocument.Parse(File.ReadAllText(_storePath));
        var entry = document.RootElement.GetProperty("Splice.Amulet@0.1.17");

        entry.GetProperty("content_hash").GetString().Should().Be("deadbeef");
        entry.GetProperty("revision").GetInt32().Should().Be(0);
    }

    [Fact]
    public void ResolveRevision_bumps_monotonically_each_time_the_content_hash_changes()
    {
        var store = JsonReleaseCounterStore.OpenOrCreate(_storePath);
        store.ResolveRevision("Splice.Amulet", new Version(0, 1, 17), "hash-a").Should().Be(0);

        store.ResolveRevision("Splice.Amulet", new Version(0, 1, 17), "hash-b").Should().Be(1);
        store.ResolveRevision("Splice.Amulet", new Version(0, 1, 17), "hash-c").Should().Be(2);
        store.ResolveRevision("Splice.Amulet", new Version(0, 1, 17), "hash-c").Should().Be(2);
    }
}

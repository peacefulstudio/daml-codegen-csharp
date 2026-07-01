// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using Daml.Codegen.CSharp.Versioning;
using AwesomeAssertions;
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
    public void ResolveGeneration_holds_generation_steady_across_repeated_calls_for_the_same_codegen_version()
    {
        var store = JsonReleaseCounterStore.OpenOrCreate(_storePath);

        store.ResolveGeneration("1.4.0").Should().Be(0);
        store.ResolveGeneration("1.4.0").Should().Be(0);
        store.ResolveGeneration("1.4.0").Should().Be(0);
    }

    [Fact]
    public void ResolveGeneration_persists_bumps_across_OpenOrCreate_reopens()
    {
        JsonReleaseCounterStore.OpenOrCreate(_storePath)
            .ResolveGeneration("1.4.0")
            .Should().Be(0);

        JsonReleaseCounterStore.OpenOrCreate(_storePath)
            .ResolveGeneration("1.4.1")
            .Should().Be(1);

        JsonReleaseCounterStore.OpenOrCreate(_storePath)
            .ResolveGeneration("1.4.1")
            .Should().Be(1);
    }

    [Fact]
    public void ResolveGeneration_tracks_each_codegen_version_independently()
    {
        var store = JsonReleaseCounterStore.OpenOrCreate(_storePath);

        store.ResolveGeneration("1.4.0").Should().Be(0);
        store.ResolveGeneration("1.4.1").Should().Be(1);
        store.ResolveGeneration("1.4.2").Should().Be(2);

        store.ResolveGeneration("1.4.0").Should().Be(0);
        store.ResolveGeneration("1.4.1").Should().Be(1);
        store.ResolveGeneration("1.4.2").Should().Be(2);
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
    public void OpenOrCreate_throws_InvalidDataException_naming_the_path_when_file_mixes_legacy_and_new_shapes()
    {
        File.WriteAllText(
            _storePath,
            "{ \"Splice.Amulet@0.1.17\": { \"content_hash\": \"abc\", \"revision\": 2 }, \"1.4.0\": 0 }");

        var action = () => JsonReleaseCounterStore.OpenOrCreate(_storePath);

        action.Should().Throw<InvalidDataException>()
            .Which.Message.Should().Contain(_storePath);
    }

    [Fact]
    public void ResolveGeneration_migrates_legacy_store_floor_to_exceed_the_highest_recorded_revision()
    {
        File.WriteAllText(
            _storePath,
            """
            {
                "Splice.Amulet@0.1.17": { "content_hash": "amulet-hash", "revision": 2 },
                "Splice.Util@0.1.5": { "content_hash": "util-hash", "revision": 1 },
                "Splice.Wallet@0.1.2": { "content_hash": "wallet-hash", "revision": 0 }
            }
            """);

        var store = JsonReleaseCounterStore.OpenOrCreate(_storePath);

        store.ResolveGeneration("1.4.0").Should().Be(3);
        store.ResolveGeneration("1.4.0").Should().Be(3);
    }

    [Fact]
    public void ResolveGeneration_does_not_carry_forward_legacy_per_package_entries()
    {
        File.WriteAllText(
            _storePath,
            "{ \"Splice.Amulet@0.1.17\": { \"content_hash\": \"amulet-hash\", \"revision\": 2 } }");

        var store = JsonReleaseCounterStore.OpenOrCreate(_storePath);
        store.ResolveGeneration("1.4.0").Should().Be(3);

        using var document = JsonDocument.Parse(File.ReadAllText(_storePath));
        var properties = document.RootElement.EnumerateObject().ToList();

        properties.Should().ContainSingle(
            "migrated legacy per-package entries must not be carried forward into the new store shape");
        properties[0].Name.Should().Be("1.4.0");
        properties[0].Value.ValueKind.Should().Be(JsonValueKind.Number);
    }

    [Fact]
    public void Persist_does_not_leave_a_dot_tmp_sibling_after_a_successful_write()
    {
        var store = JsonReleaseCounterStore.OpenOrCreate(_storePath);
        store.ResolveGeneration("1.4.0").Should().Be(0);

        File.Exists(_storePath).Should().BeTrue();
        File.Exists(_storePath + ".tmp").Should().BeFalse();
    }

    [Fact]
    public void Persist_writes_codegenVersion_key_with_flat_ordinal_value()
    {
        var store = JsonReleaseCounterStore.OpenOrCreate(_storePath);
        var generation = store.ResolveGeneration("1.4.0");

        using var document = JsonDocument.Parse(File.ReadAllText(_storePath));
        var properties = document.RootElement.EnumerateObject().ToList();

        properties.Should().ContainSingle();
        properties[0].Name.Should().Be("1.4.0");
        properties[0].Value.ValueKind.Should().Be(JsonValueKind.Number);
        properties[0].Value.GetInt32().Should().Be(generation);
    }

    [Fact]
    public void ResolveGeneration_bumps_monotonically_for_each_new_codegen_version()
    {
        var store = JsonReleaseCounterStore.OpenOrCreate(_storePath);

        store.ResolveGeneration("1.4.0").Should().Be(0);
        store.ResolveGeneration("1.4.1").Should().Be(1);
        store.ResolveGeneration("1.4.2").Should().Be(2);
        store.ResolveGeneration("1.4.2").Should().Be(2);
    }
}

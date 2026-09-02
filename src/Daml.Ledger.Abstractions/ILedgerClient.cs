// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Threading.Tasks;

namespace Daml.Ledger.Abstractions;

/// <summary>
/// A ledger client: the composition of the write, read, and streaming capabilities.
/// Derived transports (gRPC, JSON/REST) implement this whole surface.
/// </summary>
public interface ILedgerClient : ILedgerWriter, ILedgerReader, ILedgerStreamer, IDisposable, IAsyncDisposable
{
    /// <summary>
    /// Default bridge so <c>await using</c> works against any implementation:
    /// delegates to <see cref="IDisposable.Dispose"/>. Implementations that hold
    /// asynchronously-released resources (e.g. gRPC channels) should override
    /// with a genuinely asynchronous implementation.
    /// </summary>
    ValueTask IAsyncDisposable.DisposeAsync()
    {
        Dispose();
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }
}

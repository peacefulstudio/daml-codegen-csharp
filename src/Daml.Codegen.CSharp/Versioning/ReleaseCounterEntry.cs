// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Daml.Codegen.CSharp.Versioning;

/// <summary>
/// A single entry in the <see cref="JsonReleaseCounterStore"/>: the
/// content hash that produced the recorded revision, and the revision itself
/// (the 4th NuGet version segment <c>r</c> of the M.m.p.r versioning scheme).
/// </summary>
/// <param name="ContentHash">Hex-encoded content hash recorded at the last write.</param>
/// <param name="Revision">Monotonic emitter counter; non-negative.</param>
internal sealed record ReleaseCounterEntry(string ContentHash, int Revision);

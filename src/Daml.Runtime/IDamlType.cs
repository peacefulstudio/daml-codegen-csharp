// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Contracts;

namespace Daml.Runtime;

/// <summary>
/// Common marker for Daml-derived C# types. Both
/// <see cref="Daml.Runtime.Contracts.ITemplate"/> and
/// <see cref="Daml.Runtime.Contracts.IDamlInterface"/> extend it. Generic helpers
/// constrained on this broader marker accept either a concrete template type or an
/// interface marker and dispatch on the static <see cref="DamlTypeId"/> descriptor.
/// </summary>
/// <remarks>
/// The static-abstract <see cref="DamlTypeId"/> member lets helpers resolve a type's
/// identifier and kind at compile time — no reflection. Generated templates emit it as a
/// public static member; generated interfaces emit it as an explicit interface
/// implementation.
/// </remarks>
public interface IDamlType
{
    /// <summary>
    /// Gets the compile-time descriptor for this Daml type: its identifier
    /// (template id or interface id), its <see cref="DamlTypeKind"/>, and its package name.
    /// </summary>
    static abstract DamlTypeDescriptor DamlTypeId { get; }
}

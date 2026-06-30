// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Data;

namespace Daml.Runtime.Contracts;

/// <summary>
/// Discriminates whether a Daml type is a concrete template or a Daml interface,
/// selecting which identifier kind the type's <see cref="DamlTypeDescriptor"/> carries.
/// </summary>
public enum DamlTypeKind
{
    /// <summary>The type is a concrete Daml template.</summary>
    Template,

    /// <summary>The type is a Daml interface marker.</summary>
    Interface,
}

/// <summary>
/// Compile-time descriptor that lets generic helpers dispatch on a Daml type without
/// reflection: the type's <see cref="Identifier"/> (template id or interface id), its
/// <see cref="DamlTypeKind"/>, and its package name.
/// </summary>
/// <param name="Identifier">
/// The static type identifier: <see cref="ITemplate.TemplateId"/> for templates,
/// <see cref="IDamlInterface.InterfaceId"/> for interface markers.
/// </param>
/// <param name="Kind">Whether the type is a template or an interface.</param>
/// <param name="PackageName">
/// The package name containing the type. Ledger clients use this to build the
/// package-name-qualified (<c>#name</c>-style) identifier for stream / transaction
/// filters; it is bundled here so that resolution stays reflection-free at the call site.
/// In-repo serialization and matching consumers read only <see cref="Identifier"/> and
/// <see cref="Kind"/>, so this member is intentionally write-only-from-codegen until those
/// downstream filter builders consume it.
/// </param>
public readonly record struct DamlTypeDescriptor(Identifier Identifier, DamlTypeKind Kind, string PackageName);

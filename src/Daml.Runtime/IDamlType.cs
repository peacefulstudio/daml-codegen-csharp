// Copyright (c) 2026 Peaceful Studio OÜ. All rights reserved.

namespace Daml.Runtime;

/// <summary>
/// Common marker for Daml-derived C# types. Both
/// <see cref="Daml.Runtime.Contracts.ITemplate"/> and
/// <see cref="Daml.Runtime.Contracts.IDamlInterface"/> extend it. Generic helpers
/// that do not dispatch on template-specific static metadata can constrain on
/// this broader marker and accept either a concrete template type or an interface
/// marker.
/// </summary>
/// <remarks>
/// This marker has no members. Helpers that <em>do</em> dispatch on
/// template-specific static members (e.g. <c>T.TemplateId</c>) continue to
/// constrain on <see cref="Daml.Runtime.Contracts.ITemplate"/>.
/// </remarks>
public interface IDamlType
{
}

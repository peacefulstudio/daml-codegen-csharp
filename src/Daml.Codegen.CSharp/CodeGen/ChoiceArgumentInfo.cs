// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.Intermediate.Model;

namespace Daml.Codegen.CSharp.CodeGen;

/// <summary>
/// Resolved C# shape of a choice's argument, produced by
/// <see cref="ChoiceEmitter.GetChoiceArgumentInfo"/> and consumed by every exerciser,
/// command-builder, and descriptor emission path.
/// </summary>
/// <param name="TypeName">
/// The C# type name of the argument record — the sanitized choice name for a
/// same-package nested record, a resolver-qualified name for an external reference, or
/// <c>DamlUnit</c> for an argument-less choice (genuine <c>Unit</c> or the synthetic
/// stdlib <c>Archive</c>).
/// </param>
/// <param name="Fields">
/// The argument record's fields when the argument is a same-package record whose
/// definition is in scope; <c>null</c> for the argument-less shape and for external
/// references, whose definitions live in another package.
/// </param>
/// <param name="IsNestedTemplateArg">
/// True when the argument record is generated nested inside the template class, so
/// parameter types must qualify it as <c>TemplateName.TypeName</c>.
/// </param>
internal sealed record ChoiceArgumentInfo(
    string TypeName,
    IReadOnlyList<DamlFieldDefinition>? Fields,
    bool IsNestedTemplateArg)
{
    /// <summary>
    /// True when the exerciser takes an <c>argument</c> parameter — false for the
    /// argument-less <c>DamlUnit</c> shape, whose encoding the emitter supplies itself.
    /// </summary>
    public bool HasArgument => TypeName != RuntimeTypeNames.DamlUnit;

    /// <summary>
    /// The argument's parameter type at an emission site inside
    /// <paramref name="templateClassName"/>'s extensions — template-qualified for a
    /// nested record, the bare <see cref="TypeName"/> otherwise.
    /// </summary>
    public string ParameterType(string templateClassName) =>
        IsNestedTemplateArg ? $"{templateClassName}.{TypeName}" : TypeName;
}

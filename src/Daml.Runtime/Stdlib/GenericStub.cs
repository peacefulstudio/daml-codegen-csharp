// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;

namespace Daml.Runtime.Stdlib;

/// <summary>
/// Stubs for generic type-parameter cases that the C# codegen cannot fully resolve.
/// Daml supports parametric polymorphism on records (e.g. a record with a Daml
/// type variable as a field); the C# codegen has no way to call <c>.ToRecord()</c>
/// on a bare type parameter without a static-abstract dispatch path. These stubs
/// let the generated types compile so the rest of the package surface area is
/// usable, while loudly failing if anyone actually tries to serialize a generic
/// instance.
/// <para>
/// Call sites: see the <c>DamlTypeVar</c> arms of <c>GetToValueConversion</c> and
/// <c>GetFromValueConversion</c> in <c>CSharpCodeGenerator</c>. Proper
/// static-abstract dispatch for generic records is not yet implemented.
/// </para>
/// </summary>
public static class GenericStub
{
    /// <summary>
    /// Throws <see cref="NotImplementedException"/>. Marked <see cref="DoesNotReturnAttribute"/>
    /// so flow analysis treats the call site as a dead end — important because the
    /// signature returns <typeparamref name="T"/> for use in expression position
    /// (record initializers, ternaries) where C# otherwise insists on a value.
    /// </summary>
    /// <param name="context">
    /// Identifier shown in the exception message. Codegen call sites pass either the
    /// field name (in <c>GetToValueConversion</c>) or the Daml type-var name (in
    /// <c>GetFromValueConversion</c>), whichever is most useful at that site.
    /// </param>
    [DoesNotReturn]
    public static T NotImplemented<T>(string context) =>
        throw new NotImplementedException(
            $"Generic type-parameter serialization for '{context}' is not yet supported by daml-codegen-csharp. "
            + "Workaround: instantiate the type with concrete arguments and write the conversion by hand.");
}

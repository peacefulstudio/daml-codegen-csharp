// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.CSharp.Model;
using RuntimeNamespaces = Daml.Runtime.RuntimeNamespaces;

namespace Daml.Codegen.CSharp.CodeGen;

/// <summary>
/// Emits the C# for a Daml interface: the marker <c>interface</c> declaration with its
/// <see cref="Daml.Runtime.Contracts.IDamlInterface"/> facet (and the optional
/// <see cref="Daml.Runtime.Contracts.IHasView{TView}"/> facet when the interface
/// carries a view type), the static interface metadata, the per-choice method
/// signatures, and the sibling static class hosting the typed interface-choice
/// exercisers. Choice emission is delegated to the package's
/// <see cref="ChoiceEmitter"/> and view types are resolved through the package's
/// <see cref="DamlTypeMapper"/>. Constructed once per package over the package's
/// <see cref="PackageEmitContext"/>, that <see cref="DamlTypeMapper"/>, the
/// <see cref="ChoiceEmitter"/>, and the shared <see cref="CodeGenOptions"/>. The caller
/// owns the file scaffold and the common usings; this emitter writes the interface body
/// into the provided <see cref="IndentWriter"/>.
/// </summary>
public sealed class InterfaceEmitter(
    PackageEmitContext context,
    DamlTypeMapper mapper,
    ChoiceEmitter choiceEmitter,
    CodeGenOptions options)
{
    /// <summary>
    /// Writes the interface declaration, its static metadata, the per-choice method
    /// signatures, and the sibling interface-choice exerciser class for
    /// <paramref name="iface"/> into <paramref name="indent"/>.
    /// </summary>
    internal void WriteInterfaceType(IndentWriter indent, DamlPackage package, DamlModule module, DamlInterface iface)
    {
        var dataTypes = context.DataTypes;

        if (options.GenerateXmlDocs)
        {
            indent.AppendLine("/// <summary>");
            indent.AppendLine($"/// Generated from Daml interface {module.Name}:{iface.Name}");
            indent.AppendLine("/// </summary>");
        }

        var interfaceName = Identifiers.InterfaceMarkerName(iface.Name);
        indent.CurrentTypeName = interfaceName;

        var viewType = iface.ViewType is not null ? mapper.MapType(iface.ViewType) : null;
        var interfaces = viewType is not null
            ? $"{context.Qualifier.Qualify(RuntimeTypeNames.IDamlInterface, context.RootNamespace)}, {context.Qualifier.Qualify(RuntimeTypeNames.IHasView, context.RootNamespace)}<{viewType}>"
            : context.Qualifier.Qualify(RuntimeTypeNames.IDamlInterface, context.RootNamespace);

        indent.AppendLine($"public interface {interfaceName} : {interfaces}");
        indent.AppendLine("{");
        indent.Indent();

        WriteInterfaceMetadata(indent, package, module, iface);

        foreach (var method in iface.Choices)
        {
            choiceEmitter.WriteInterfaceMethod(indent, method, dataTypes);
        }

        indent.Dedent();
        indent.AppendLine("}");

        if (iface.Choices.Count > 0)
        {
            indent.AppendLine();
            choiceEmitter.WriteInterfaceChoiceExtensions(indent, iface, interfaceName);
        }
    }

    private void WriteInterfaceMetadata(IndentWriter indent, DamlPackage package, DamlModule module, DamlInterface iface)
    {
        indent.Require("System");
        indent.Require(RuntimeNamespaces.Contracts);

        if (options.GenerateXmlDocs)
        {
            indent.AppendLine("/// <summary>Gets the interface identifier.</summary>");
        }
        indent.AppendLine($"static {context.Qualifier.Qualify(RuntimeTypeNames.Identifier, context.RootNamespace)} {context.Qualifier.Qualify(RuntimeTypeNames.IDamlInterface, context.RootNamespace)}.InterfaceId => new(\"{package.PackageId}\", \"{module.Name}\", \"{iface.Name}\");");
        indent.AppendLine();

        if (options.GenerateXmlDocs)
        {
            indent.AppendLine("/// <summary>Gets the package ID.</summary>");
        }
        indent.AppendLine($"static string {context.Qualifier.Qualify(RuntimeTypeNames.IDamlInterface, context.RootNamespace)}.{nameof(Daml.Runtime.Contracts.IDamlInterface.PackageId)} => \"{package.PackageId}\";");
        indent.AppendLine();

        if (options.GenerateXmlDocs)
        {
            indent.AppendLine("/// <summary>Gets the package name.</summary>");
        }
        indent.AppendLine($"static string {context.Qualifier.Qualify(RuntimeTypeNames.IDamlInterface, context.RootNamespace)}.{nameof(Daml.Runtime.Contracts.IDamlInterface.PackageName)} => \"{package.Name}\";");
        indent.AppendLine();

        if (options.GenerateXmlDocs)
        {
            indent.AppendLine("/// <summary>Gets the package version.</summary>");
        }
        indent.AppendLine($"static Version {context.Qualifier.Qualify(RuntimeTypeNames.IDamlInterface, context.RootNamespace)}.{nameof(Daml.Runtime.Contracts.IDamlInterface.PackageVersion)} => new({package.Version.Major}, {package.Version.Minor}, {package.Version.Build});");
        indent.AppendLine();

        var descriptorType = context.Qualifier.Qualify(RuntimeTypeNames.DamlTypeDescriptor, context.RootNamespace);
        var kindType = context.Qualifier.Qualify(RuntimeTypeNames.DamlTypeKind, context.RootNamespace);
        var identifierType = context.Qualifier.Qualify(RuntimeTypeNames.Identifier, context.RootNamespace);
        if (options.GenerateXmlDocs)
        {
            indent.AppendLine("/// <summary>Gets the compile-time Daml type descriptor.</summary>");
        }
        indent.AppendLine($"static {descriptorType} global::Daml.Runtime.IDamlType.DamlTypeId => new(new {identifierType}(\"{package.PackageId}\", \"{module.Name}\", \"{iface.Name}\"), {kindType}.Interface, \"{package.Name}\");");
        indent.AppendLine();
    }
}

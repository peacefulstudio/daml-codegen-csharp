// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.Intermediate.Model;
using RuntimeNamespaces = Daml.Runtime.RuntimeNamespaces;

namespace Daml.Codegen.CSharp.CodeGen;

/// <summary>
/// Emits the C# for a Daml interface: the marker <c>interface</c> declaration with its
/// <see cref="Daml.Runtime.Contracts.IDamlInterface"/> facet (and the optional
/// <see cref="Daml.Runtime.Contracts.IHasView{TView}"/> facet when the interface
/// carries a view type), the static interface metadata, the view enrichment (the static
/// <c>View</c> witness for any view type that resolves to a non-generic record, see
/// <see cref="PackageEmitContext.HasWitnessableViewRecord"/>; plus instance properties
/// mirroring the view's fields when that record is package-local and viewed by exactly
/// one interface, see <see cref="PackageEmitContext.LocalViewRecordMarkerNames"/> for
/// that boundary), the per-choice method signatures, and the sibling static class hosting
/// the typed interface-choice exercisers. Choice emission is delegated to the package's
/// <see cref="ChoiceEmitter"/> and view types are resolved through the package's
/// <see cref="DamlTypeMapper"/>. Constructed once per package over the package's
/// <see cref="PackageEmitContext"/>, that <see cref="DamlTypeMapper"/>, the DAR-scoped
/// <see cref="ICrossPackageResolver"/>, the <see cref="ChoiceEmitter"/>, and the shared
/// <see cref="CodeGenOptions"/>. The caller owns the file scaffold and the common
/// usings; this emitter writes the interface body into the provided
/// <see cref="IndentWriter"/>.
/// </summary>
internal sealed class InterfaceEmitter(
    PackageEmitContext context,
    DamlTypeMapper mapper,
    ICrossPackageResolver resolver,
    ChoiceEmitter choiceEmitter,
    CodeGenOptions options)
{
    /// <summary>
    /// Writes the interface declaration, its static metadata, its view enrichment, the
    /// per-choice method signatures, and the sibling interface-choice exerciser class for
    /// <paramref name="iface"/> into <paramref name="indent"/>.
    /// </summary>
    internal void WriteInterfaceType(IndentWriter indent, DamlPackage package, DamlModule module, DamlInterface iface)
    {
        var interfaceName = context.LocalInterfaceMarkerNames[$"{module.Name}:{iface.Name}"];
        var viewType = iface.ViewType is not null ? mapper.MapType(iface.ViewType) : null;
        var stampedViewRecord = StampedViewRecordDefinition(iface);

        if (options.GenerateXmlDocs)
        {
            indent.AppendLine("/// <summary>");
            indent.AppendLine($"/// Generated from Daml interface {module.Name}:{iface.Name}");
            indent.AppendLine("/// </summary>");
            if (stampedViewRecord is not null)
            {
                indent.AppendLine("/// <remarks>");
                indent.AppendLine($"/// Instance properties mirror the fields of the interface view <see cref=\"{viewType}\"/>,");
                indent.AppendLine("/// which implements this marker, so a view can be read through a marker-typed variable.");
                indent.AppendLine("/// <c>==</c> between marker-typed variables compares by reference equality; view payloads");
                indent.AppendLine($"/// materialize as concrete <see cref=\"{viewType}\"/> values, whose record value equality");
                indent.AppendLine("/// applies once concretely typed.");
                indent.AppendLine("/// </remarks>");
            }
        }

        indent.CurrentTypeName = interfaceName;

        var interfaces = viewType is not null
            ? $"{context.Qualifier.Qualify(RuntimeTypeNames.IDamlInterface, context.RootNamespace)}, {context.Qualifier.Qualify(RuntimeTypeNames.IHasView, context.RootNamespace)}<{viewType}>"
            : context.Qualifier.Qualify(RuntimeTypeNames.IDamlInterface, context.RootNamespace);

        indent.AppendLine($"public interface {interfaceName} : {interfaces}");
        indent.AppendLine("{");
        indent.Indent();

        WriteInterfaceMetadata(indent, package, module, iface);

        if (viewType is not null && context.HasWitnessableViewRecord(iface))
        {
            WriteViewWitness(indent, interfaceName, viewType);
        }

        if (stampedViewRecord is not null)
        {
            WriteViewFieldProperties(indent, stampedViewRecord);
        }

        indent.Dedent();
        indent.AppendLine("}");

        if (iface.Choices.Count > 0)
        {
            indent.AppendLine();
            choiceEmitter.WriteInterfaceChoiceExtensions(indent, iface, interfaceName);
        }
    }

    private DamlRecordDefinition? StampedViewRecordDefinition(DamlInterface iface) =>
        iface.ViewType is DamlTypeRef viewRef
        && context.IsLocalRef(viewRef)
        && context.LocalViewRecordMarkerNames.ContainsKey($"{viewRef.Module}:{viewRef.Name}")
            ? context.LocalViewRecord(viewRef)
            : null;

    private void WriteViewWitness(IndentWriter indent, string interfaceName, string viewTypeName)
    {
        var descriptorType = context.Qualifier.Qualify(RuntimeTypeNames.ViewDescriptor, context.RootNamespace);
        if (options.GenerateXmlDocs)
        {
            indent.AppendLine($"/// <summary>Gets the pure type witness pairing this marker with its view record <see cref=\"{viewTypeName}\"/>; passing it to a generic method infers both type parameters from one argument.</summary>");
        }
        indent.AppendLine($"public static {descriptorType}<{interfaceName}, {viewTypeName}> View {{ get; }} = new();");
        indent.AppendLine();
    }

    private void WriteViewFieldProperties(IndentWriter indent, DamlRecordDefinition viewRecord)
    {
        foreach (var field in viewRecord.Fields)
        {
            var csharpType = mapper.MapType(field.Type);
            var memberName = Identifiers.MemberName(field.Name, indent.CurrentTypeName);
            StdlibPackages.RequireForFieldType(resolver, context.Package, indent, field.Type);
            if (options.GenerateXmlDocs)
            {
                indent.AppendLine($"/// <summary>Gets the {field.Name} field of the interface view.</summary>");
            }
            indent.AppendLine($"{csharpType} {memberName} {{ get; }}");
            indent.AppendLine();
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
        indent.AppendLine($"static {context.Qualifier.Qualify(RuntimeTypeNames.Identifier, context.RootNamespace)} {context.Qualifier.Qualify(RuntimeTypeNames.IDamlInterface, context.RootNamespace)}.InterfaceId => InterfaceId;");
        indent.AppendLine();

        if (options.GenerateXmlDocs)
        {
            indent.AppendLine("/// <summary>Gets the interface identifier.</summary>");
        }
        indent.AppendLine($"public static new {context.Qualifier.Qualify(RuntimeTypeNames.Identifier, context.RootNamespace)} InterfaceId {{ get; }} = new(\"{package.PackageId}\", \"{module.Name}\", \"{iface.Name}\");");
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

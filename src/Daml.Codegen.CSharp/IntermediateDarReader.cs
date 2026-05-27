// Copyright (c) 2026 Peaceful Studio OÜ. All rights reserved.

using Daml.Codegen.CSharp.Model;
using PbBuiltin = Daml.Codegen.Intermediate.BuiltinType;
using PbChoice = Daml.Codegen.Intermediate.Choice;
using PbDar = Daml.Codegen.Intermediate.IntermediateDar;
using PbDataType = Daml.Codegen.Intermediate.DataType;
using PbField = Daml.Codegen.Intermediate.Field;
using PbInterface = Daml.Codegen.Intermediate.Interface;
using PbModule = Daml.Codegen.Intermediate.IntermediateModule;
using PbPackage = Daml.Codegen.Intermediate.IntermediatePackage;
using PbTemplate = Daml.Codegen.Intermediate.Template;
using PbType = Daml.Codegen.Intermediate.Type;
using PbTypeConName = Daml.Codegen.Intermediate.TypeConName;

namespace Daml.Codegen.CSharp;

/// <summary>
/// Maps a <see cref="PbDar"/> protobuf message (produced by the JVM helper,
/// per ADR 0003 and the schema in <c>proto/intermediate_dar.proto</c>) into
/// the emitter's in-memory <see cref="DarModel"/>. Static party analysis
/// (<c>signatories</c>, <c>observers</c>, <c>controllers</c>) is
/// intentionally absent from the proto — schema-mode decode strips
/// expression bodies — so this adapter sets
/// <see cref="DamlPartyAnalysis.Dynamic"/> on every template and choice.
/// Callers that need static analysis must drive the emitter via the
/// parser-direct path (<c>DarArchive.ReadAsync</c>) in
/// <c>Daml.Codegen.DarParser</c> instead.
/// </summary>
public static class IntermediateDarReader
{
    public static DarModel Read(PbDar proto)
    {
        ArgumentNullException.ThrowIfNull(proto);
        if (proto.Main is null)
            throw new InvalidDataException("IntermediateDar.main is required");

        var main = ConvertPackage(proto.Main);
        var deps = proto.Dependencies.Select(ConvertPackage).ToList();

        return new DarModel { MainPackage = main, Dependencies = deps };
    }

    private static DamlPackage ConvertPackage(PbPackage pkg) =>
        new()
        {
            PackageId = pkg.PackageId,
            Name = pkg.PackageName,
            Version = ParseVersion(pkg.PackageVersion),
            LfVersion = pkg.LanguageVersion,
            Modules = pkg.Modules.Select(ConvertModule).ToList(),
            DependencyReferences = [],
            UpgradedPackageId = string.IsNullOrEmpty(pkg.UpgradedPackageId) ? null : pkg.UpgradedPackageId,
        };

    private static DamlModule ConvertModule(PbModule module)
    {
        var dataTypes = module.DataTypes.Select(ConvertDataType).ToList();
        var dataTypesByName = dataTypes.ToDictionary(dt => dt.Name);
        var templates = module.Templates.Select(t => ConvertTemplate(t, dataTypesByName)).ToList();
        var interfaces = module.Interfaces.Select(ConvertInterface).ToList();

        return new DamlModule
        {
            Name = string.Join(".", module.NameSegments),
            Templates = templates,
            DataTypes = dataTypes,
            Interfaces = interfaces,
        };
    }

    private static DamlDataType ConvertDataType(PbDataType dt)
    {
        DamlDataTypeDefinition definition = dt.ShapeCase switch
        {
            PbDataType.ShapeOneofCase.Record =>
                new DamlRecordDefinition(dt.Record.Fields.Select(ConvertField).ToList()),
            PbDataType.ShapeOneofCase.Variant =>
                new DamlVariantDefinition(dt.Variant.Constructors.Select(ConvertVariantConstructor).ToList()),
            PbDataType.ShapeOneofCase.EnumType =>
                new DamlEnumDefinition(dt.EnumType.Constructors.ToList()),
            PbDataType.ShapeOneofCase.None => throw new InvalidDataException(
                $"IntermediateDar DataType '{dt.Name}' has no shape set — every DataType must populate exactly one of {{record, variant, enum_type}}."),
            _ => throw new InvalidDataException(
                $"IntermediateDar DataType '{dt.Name}' has unknown shape '{dt.ShapeCase}'. " +
                "Update IntermediateDarReader.ConvertDataType to handle the new shape, alongside the proto schema and the JVM helper."),
        };

        return new DamlDataType
        {
            Name = dt.Name,
            TypeParams = dt.TypeParameters.ToList(),
            Serializable = dt.IsSerializable,
            Definition = definition,
        };
    }

    private static DamlField ConvertField(PbField field)
    {
        if (field.Type is null)
            throw new InvalidDataException(
                $"IntermediateDar Field '{field.Name}' is missing type — every field must declare its type.");
        return new DamlField(field.Name, ConvertType(field.Type));
    }

    private static DamlVariantConstructor ConvertVariantConstructor(PbField field)
    {
        if (field.Type is null)
            throw new InvalidDataException(
                $"IntermediateDar Variant constructor '{field.Name}' is missing type — every constructor must declare its argument type (use BuiltinType.UNIT for a no-arg constructor).");
        return new DamlVariantConstructor(field.Name, ConvertType(field.Type));
    }

    private static DamlTemplate ConvertTemplate(PbTemplate template, IReadOnlyDictionary<string, DamlDataType> dataTypesByName)
    {
        var fields = dataTypesByName.TryGetValue(template.Name, out var dt)
                     && dt.Definition is DamlRecordDefinition recordDef
            ? recordDef.Fields
            : [];

        return new DamlTemplate
        {
            Name = template.Name,
            Fields = fields,
            Choices = template.Choices.Select(ConvertChoice).ToList(),
            Key = template.KeyType is not null ? ConvertType(template.KeyType) : null,
            Implements = template.Implements.Select(FormatTypeConName).ToList(),
        };
    }

    private static DamlChoice ConvertChoice(PbChoice choice)
    {
        if (choice.ArgumentType is null)
            throw new InvalidDataException(
                $"IntermediateDar Choice '{choice.Name}' is missing argument_type — every choice must declare its argument type (use BuiltinType.UNIT for a no-arg choice).");
        if (choice.ReturnType is null)
            throw new InvalidDataException(
                $"IntermediateDar Choice '{choice.Name}' is missing return_type — every choice must declare its return type (use BuiltinType.UNIT for a no-result choice).");

        return new DamlChoice
        {
            Name = choice.Name,
            Consuming = choice.Consuming,
            ArgumentType = ConvertType(choice.ArgumentType),
            ReturnType = ConvertType(choice.ReturnType),
        };
    }

    private static DamlInterface ConvertInterface(PbInterface iface) =>
        new()
        {
            Name = iface.Name,
            Choices = iface.Choices.Select(ConvertChoice).ToList(),
            ViewType = iface.ViewType is not null ? ConvertType(iface.ViewType) : null,
        };

    private static DamlType ConvertType(PbType type)
    {
        ArgumentNullException.ThrowIfNull(type);

        return type.SortCase switch
        {
            PbType.SortOneofCase.Builtin => new DamlPrimitiveType(ConvertBuiltin(type.Builtin)),
            PbType.SortOneofCase.TypeCon => ConvertTypeConRef(type.TypeCon),
            PbType.SortOneofCase.TypeApp => ConvertTypeApp(type),
            PbType.SortOneofCase.TypeVar => new DamlTypeVar(type.TypeVar),
            PbType.SortOneofCase.Nat => new DamlTypeVar(type.Nat.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            PbType.SortOneofCase.None => throw new InvalidDataException(
                "IntermediateDar Type has no sort set — every Type must populate exactly one of {builtin, type_con, type_app, type_var, nat}."),
            _ => throw new InvalidDataException(
                $"IntermediateDar Type has unknown sort '{type.SortCase}'. " +
                "Update IntermediateDarReader.ConvertType to handle the new sort, alongside the proto schema and the JVM helper."),
        };
    }

    private static DamlTypeApp ConvertTypeApp(PbType type)
    {
        if (type.TypeApp.Function is null)
            throw new InvalidDataException(
                "IntermediateDar TypeApp is missing function — every type application must declare the head type being applied.");
        var head = ConvertType(type.TypeApp.Function);
        var args = type.TypeApp.Arguments.Select(arg =>
        {
            if (arg is null)
                throw new InvalidDataException(
                    "IntermediateDar TypeApp has a null entry in arguments — every type-app argument must be a populated Type.");
            return ConvertType(arg);
        }).ToList();
        return new DamlTypeApp(head, args);
    }

    private static DamlTypeRef ConvertTypeConRef(PbTypeConName tcn) =>
        new(tcn.PackageId, string.Join(".", tcn.ModuleNameSegments), string.Join(".", tcn.NameSegments));

    private static string FormatTypeConName(PbTypeConName tcn) =>
        $"{string.Join(".", tcn.ModuleNameSegments)}.{string.Join(".", tcn.NameSegments)}";

    private static DamlPrimitive ConvertBuiltin(PbBuiltin builtin) => builtin switch
    {
        PbBuiltin.Unit => DamlPrimitive.Unit,
        PbBuiltin.Bool => DamlPrimitive.Bool,
        PbBuiltin.Int64 => DamlPrimitive.Int64,
        PbBuiltin.Text => DamlPrimitive.Text,
        PbBuiltin.Numeric => DamlPrimitive.Numeric,
        PbBuiltin.Party => DamlPrimitive.Party,
        PbBuiltin.Date => DamlPrimitive.Date,
        PbBuiltin.Timestamp => DamlPrimitive.Timestamp,
        PbBuiltin.List => DamlPrimitive.List,
        PbBuiltin.Optional => DamlPrimitive.Optional,
        PbBuiltin.TextMap => DamlPrimitive.TextMap,
        PbBuiltin.GenMap => DamlPrimitive.GenMap,
        PbBuiltin.ContractId => DamlPrimitive.ContractId,
        PbBuiltin.Unspecified => throw new InvalidDataException(
            "IntermediateDar BuiltinType is BUILTIN_TYPE_UNSPECIFIED — every BuiltinType must be set to a defined value by the producer."),
        _ => throw new NotSupportedException(
            $"IntermediateDar BuiltinType '{builtin}' has no C# mapping yet. " +
            "Update IntermediateDarReader.ConvertBuiltin (and the emitter, runtime, and JSON serializer) to add the mapping."),
    };

    private static Version ParseVersion(string raw) =>
        PackageVersionParser.Parse(raw);
}

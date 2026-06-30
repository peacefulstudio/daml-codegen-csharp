// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using AwesomeAssertions;
using Xunit;

namespace Daml.Runtime.Tests;

public partial class ContractTypesTests
{
    [Fact]
    public void ITemplate_static_properties_should_be_accessible()
    {
        TestTemplate.TemplateId.PackageId.Should().Be(TestPackageId);
        TestTemplate.TemplateId.ModuleName.Should().Be(TestModuleName);
        TestTemplate.TemplateId.EntityName.Should().Be(nameof(TestTemplate));
        TestTemplate.PackageId.Should().Be(TestPackageId);
        TestTemplate.PackageName.Should().Be(TestPackageName);
        TestTemplate.PackageVersion.Should().Be(TestPackageV1);
    }

    [Fact]
    public void ITemplate_ToRecord_should_serialize_correctly()
    {
        var template = new TestTemplate(new Party("Charlie"), 500);

        var record = template.ToRecord();

        record.GetField("owner")!.As<DamlParty>().Value.Should().Be("Charlie");
        record.GetField("amount")!.As<DamlInt64>().Value.Should().Be(500);
    }

    [Fact]
    public void ITemplate_FromRecord_should_deserialize_correctly()
    {
        var record = DamlRecord.Create(
            DamlField.Create("owner", new DamlParty("Diana")),
            DamlField.Create("amount", new DamlInt64(750)));

        var template = TestTemplate.FromRecord(record);

        template.Owner.Should().Be(new Party("Diana"));
        template.Amount.Should().Be(750);
    }

    private sealed record KeyedTemplate(Party Owner, string AssetId) : ITemplate, IHasKey<string>
    {
        public static Identifier TemplateId => new(TestPackageId, TestModuleName, nameof(KeyedTemplate));
        public static string PackageId => TestPackageId;
        public static string PackageName => TestPackageName;
        public static Version PackageVersion => TestPackageV1;
        public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);
        public string Key => AssetId;

        public DamlRecord ToRecord() => DamlRecord.Create(
            DamlField.Create("owner", Owner.ToDamlValue()),
            DamlField.Create("assetId", new DamlText(AssetId)));

        public static KeyedTemplate FromRecord(DamlRecord record) =>
            new(Party.FromDamlValue(record.GetRequiredField("owner").As<DamlParty>()),
                record.GetRequiredField("assetId").As<DamlText>().Value);
    }

    [Fact]
    public void IHasKey_should_return_contract_key()
    {
        var template = new KeyedTemplate(new Party("Alice"), "asset-123");

        var key = template.Key;

        key.Should().Be("asset-123");
    }

    [Fact]
    public void IHasKey_should_be_covariant_with_key_type()
    {
        IHasKey<string> keyedContract = new KeyedTemplate(new Party("Bob"), "asset-456");

        var key = keyedContract.Key;

        key.Should().Be("asset-456");
    }

    private interface ITestInterface : IDamlInterface
    {
        static Identifier IDamlInterface.InterfaceId => new(TestPackageId, TestModuleName, "TestInterface");
        static string IDamlInterface.PackageId => TestPackageId;
        static string IDamlInterface.PackageName => TestPackageName;
        static Version IDamlInterface.PackageVersion => new(2, 0, 0);
        static DamlTypeDescriptor global::Daml.Runtime.IDamlType.DamlTypeId =>
            new(new Identifier(TestPackageId, TestModuleName, "TestInterface"), DamlTypeKind.Interface, TestPackageName);
    }

    [Fact]
    public void IDamlInterface_should_provide_interface_metadata()
    {
        var interfaceId = GetInterfaceId<ITestInterface>();
        interfaceId.PackageId.Should().Be(TestPackageId);
        interfaceId.ModuleName.Should().Be(TestModuleName);
        interfaceId.EntityName.Should().Be("TestInterface");
    }

    private static Identifier GetInterfaceId<T>() where T : IDamlInterface
    {
        return T.InterfaceId;
    }

    private sealed record AssetView(Party Owner, decimal Amount);

    private sealed record ViewedTemplate(Party Owner, decimal Amount) : ITemplate, IHasView<AssetView>
    {
        public static Identifier TemplateId => new(TestPackageId, TestModuleName, nameof(ViewedTemplate));
        public static string PackageId => TestPackageId;
        public static string PackageName => TestPackageName;
        public static Version PackageVersion => TestPackageV1;
        public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);
        public AssetView View => new(Owner, Amount);

        public DamlRecord ToRecord() => DamlRecord.Create(
            DamlField.Create("owner", Owner.ToDamlValue()),
            DamlField.Create("amount", new DamlNumeric(Amount)));

        public static ViewedTemplate FromRecord(DamlRecord record) =>
            new(Party.FromDamlValue(record.GetRequiredField("owner").As<DamlParty>()),
                record.GetRequiredField("amount").As<DamlNumeric>().Value);
    }

    [Fact]
    public void IHasView_should_return_interface_view()
    {
        var template = new ViewedTemplate(new Party("Charlie"), 1000.50m);

        var view = template.View;

        view.Owner.Should().Be(new Party("Charlie"));
        view.Amount.Should().Be(1000.50m);
    }

    [Fact]
    public void IHasView_should_be_covariant_with_view_type()
    {
        IHasView<AssetView> viewable = new ViewedTemplate(new Party("Diana"), 2500.00m);

        var view = viewable.View;

        view.Owner.Should().Be(new Party("Diana"));
        view.Amount.Should().Be(2500.00m);
    }

    private const string UpgradedPkgId = "upgraded-package";
    private const string UpgradedPackageName = "upgraded-package-name";

    private sealed record UpgradeableTemplate(string Value) : ITemplate, IUpgradeable
    {
        public static Identifier TemplateId => new(UpgradedPkgId, TestModuleName, nameof(UpgradeableTemplate));
        public static string PackageId => UpgradedPkgId;
        public static string PackageName => UpgradedPackageName;
        public static Version PackageVersion => new(2, 0, 0);
        public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);
        public static string? UpgradedPackageId => "previous-package-id-12345";

        public DamlRecord ToRecord() => DamlRecord.Create(
            DamlField.Create("value", new DamlText(Value)));

        public static UpgradeableTemplate FromRecord(DamlRecord record) =>
            new(record.GetRequiredField("value").As<DamlText>().Value);
    }

    private sealed record NonUpgradeableTemplate(string Value) : ITemplate
    {
        public static Identifier TemplateId => new("new-package", TestModuleName, nameof(NonUpgradeableTemplate));
        public static string PackageId => "new-package";
        public static string PackageName => "new-package-name";
        public static Version PackageVersion => TestPackageV1;
        public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);

        public DamlRecord ToRecord() => DamlRecord.Create(
            DamlField.Create("value", new DamlText(Value)));

        public static NonUpgradeableTemplate FromRecord(DamlRecord record) =>
            new(record.GetRequiredField("value").As<DamlText>().Value);
    }

    [Fact]
    public void IUpgradeable_should_provide_upgraded_package_id()
    {
        UpgradeableTemplate.UpgradedPackageId.Should().Be("previous-package-id-12345");
    }

    [Fact]
    public void IUpgradeable_should_be_accessible_via_generic_constraint()
    {
        var upgradedId = GetUpgradedPackageId<UpgradeableTemplate>();

        upgradedId.Should().Be("previous-package-id-12345");
    }

    private static string? GetUpgradedPackageId<T>() where T : IUpgradeable
    {
        return T.UpgradedPackageId;
    }

    [Fact]
    public void NonUpgradeable_template_should_not_implement_IUpgradeable()
    {
        typeof(NonUpgradeableTemplate).GetInterfaces()
            .Should().NotContain(typeof(IUpgradeable));
    }

    private interface ITransferable : IDamlInterface
    {
        static Identifier IDamlInterface.InterfaceId => new(TestPackageId, TestModuleName, "Transferable");
        static string IDamlInterface.PackageId => TestPackageId;
        static string IDamlInterface.PackageName => TestPackageName;
        static Version IDamlInterface.PackageVersion => TestPackageV1;
        static DamlTypeDescriptor global::Daml.Runtime.IDamlType.DamlTypeId =>
            new(new Identifier(TestPackageId, TestModuleName, "Transferable"), DamlTypeKind.Interface, TestPackageName);
    }

    private sealed record TransferableAsset(Party Owner, decimal Amount) : ITemplate, IImplements<ITransferable>
    {
        public static Identifier TemplateId => new(TestPackageId, TestModuleName, nameof(TransferableAsset));
        public static string PackageId => TestPackageId;
        public static string PackageName => TestPackageName;
        public static Version PackageVersion => TestPackageV1;
        public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);

        public DamlRecord ToRecord() => DamlRecord.Create(
            DamlField.Create("owner", Owner.ToDamlValue()),
            DamlField.Create("amount", new DamlNumeric(Amount)));

        public static TransferableAsset FromRecord(DamlRecord record) =>
            new(Party.FromDamlValue(record.GetRequiredField("owner").As<DamlParty>()),
                record.GetRequiredField("amount").As<DamlNumeric>().Value);
    }

    [Fact]
    public void IImplements_should_mark_template_as_implementing_interface()
    {
        typeof(TransferableAsset).GetInterfaces()
            .Should().Contain(typeof(IImplements<ITransferable>));
    }

    [Fact]
    public void IImplements_template_should_be_assignable_to_IImplements()
    {
        var asset = new TransferableAsset(new Party("Eve"), 5000m);

        asset.Should().BeAssignableTo<IImplements<ITransferable>>();
    }

    [Fact]
    public void GetTemplateId_should_return_package_name_format_by_default()
    {
        var templateId = TemplateExtensions.GetTemplateId<TestTemplate>();

        templateId.Should().Be($"{TestPackageName}:{TestModuleName}:{nameof(TestTemplate)}");
    }

    [Fact]
    public void GetTemplateId_with_PackageHash_format_should_return_hash_format()
    {
        var templateId = TemplateExtensions.GetTemplateId<TestTemplate>(TemplateIdFormat.PackageHash);

        templateId.Should().Be($"{TestPackageId}:{TestModuleName}:{nameof(TestTemplate)}");
    }

    [Fact]
    public void GetTemplateId_extension_method_should_return_package_name_format_by_default()
    {
        var template = new TestTemplate(new Party("Alice"), 100);

        var templateId = template.GetTemplateId();

        templateId.Should().Be($"{TestPackageName}:{TestModuleName}:{nameof(TestTemplate)}");
    }

    [Fact]
    public void GetTemplateId_extension_method_with_PackageHash_format_should_return_hash_format()
    {
        var template = new TestTemplate(new Party("Alice"), 100);

        var templateId = template.GetTemplateId(TemplateIdFormat.PackageHash);

        templateId.Should().Be($"{TestPackageId}:{TestModuleName}:{nameof(TestTemplate)}");
    }

    [Fact]
    public void GetTemplateId_should_work_with_different_templates()
    {
        var keyedTemplateId = TemplateExtensions.GetTemplateId<KeyedTemplate>();
        var viewedTemplateId = TemplateExtensions.GetTemplateId<ViewedTemplate>();
        var upgradeableTemplateId = TemplateExtensions.GetTemplateId<UpgradeableTemplate>();

        keyedTemplateId.Should().Be($"{TestPackageName}:{TestModuleName}:{nameof(KeyedTemplate)}");
        viewedTemplateId.Should().Be($"{TestPackageName}:{TestModuleName}:{nameof(ViewedTemplate)}");
        upgradeableTemplateId.Should().Be($"{UpgradedPackageName}:{TestModuleName}:{nameof(UpgradeableTemplate)}");
    }

    [Fact]
    public void GetTemplateId_with_PackageHash_format_should_work_with_different_templates()
    {
        var keyedTemplateId = TemplateExtensions.GetTemplateId<KeyedTemplate>(TemplateIdFormat.PackageHash);
        var viewedTemplateId = TemplateExtensions.GetTemplateId<ViewedTemplate>(TemplateIdFormat.PackageHash);
        var upgradeableTemplateId = TemplateExtensions.GetTemplateId<UpgradeableTemplate>(TemplateIdFormat.PackageHash);

        keyedTemplateId.Should().Be($"{TestPackageId}:{TestModuleName}:{nameof(KeyedTemplate)}");
        viewedTemplateId.Should().Be($"{TestPackageId}:{TestModuleName}:{nameof(ViewedTemplate)}");
        upgradeableTemplateId.Should().Be($"{UpgradedPkgId}:{TestModuleName}:{nameof(UpgradeableTemplate)}");
    }

    [Fact]
    public void GetTemplateId_extension_should_return_same_result_as_static_method()
    {
        var template = new KeyedTemplate(new Party("Bob"), "asset-123");

        var staticResult = TemplateExtensions.GetTemplateId<KeyedTemplate>();
        var extensionResult = template.GetTemplateId();

        staticResult.Should().Be(extensionResult);
    }

    [Fact]
    public void GetTemplateId_extension_with_PackageHash_format_should_return_same_result_as_static_method()
    {
        var template = new KeyedTemplate(new Party("Bob"), "asset-123");

        var staticResult = TemplateExtensions.GetTemplateId<KeyedTemplate>(TemplateIdFormat.PackageHash);
        var extensionResult = template.GetTemplateId(TemplateIdFormat.PackageHash);

        staticResult.Should().Be(extensionResult);
    }

    private sealed record EmptyPackageNameTemplate : ITemplate
    {
        public static Identifier TemplateId => new(TestPackageId, TestModuleName, nameof(EmptyPackageNameTemplate));
        public static string PackageId => TestPackageId;
        public static string PackageName => string.Empty;
        public static Version PackageVersion => TestPackageV1;
        public static DamlTypeDescriptor DamlTypeId { get; } = new(TemplateId, DamlTypeKind.Template, PackageName);

        public DamlRecord ToRecord() => DamlRecord.Create();
        public static EmptyPackageNameTemplate FromRecord(DamlRecord record) => new();
    }

    [Fact]
    public void GetTemplateId_should_throw_for_empty_PackageName_instead_of_silently_falling_back_to_hash_format()
    {
        var act = () => TemplateExtensions.GetTemplateId<EmptyPackageNameTemplate>();

        act.Should().Throw<InvalidOperationException>().WithMessage($"*{nameof(ITemplate.PackageName)}*");
    }
}

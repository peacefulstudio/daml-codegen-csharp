using Daml.Runtime.Data;

namespace Daml.Runtime.Contracts;

/// <summary>
/// Marker interface for all Daml templates.
/// </summary>
public interface ITemplate : IDamlValue
{
    /// <summary>
    /// Gets the template identifier for this template type.
    /// </summary>
    static abstract Identifier TemplateId { get; }

    /// <summary>
    /// Gets the package ID containing this template.
    /// </summary>
    static abstract string PackageId { get; }

    /// <summary>
    /// Gets the package name containing this template.
    /// </summary>
    static abstract string PackageName { get; }

    /// <summary>
    /// Gets the package version.
    /// </summary>
    static abstract Version PackageVersion { get; }
}

/// <summary>
/// Interface for templates that have a contract key.
/// </summary>
/// <typeparam name="TKey">The key type.</typeparam>
public interface IHasKey<TKey>
{
    /// <summary>
    /// Gets the contract key.
    /// </summary>
    TKey Key { get; }
}

/// <summary>
/// Companion object pattern for templates - provides static factory methods.
/// </summary>
/// <typeparam name="T">The template type.</typeparam>
public interface ITemplateCompanion<T> where T : ITemplate
{
    /// <summary>
    /// Decodes a template instance from a Daml record.
    /// </summary>
    static abstract T FromRecord(DamlRecord record);

    /// <summary>
    /// Decodes a template instance from JSON.
    /// </summary>
    static abstract T FromJson(string json);
}

/// <summary>
/// Marker interface for all Daml interfaces.
/// </summary>
public interface IDamlInterface : IDamlValue
{
    /// <summary>
    /// Gets the interface identifier for this interface type.
    /// </summary>
    static abstract Identifier InterfaceId { get; }

    /// <summary>
    /// Gets the package ID containing this interface.
    /// </summary>
    static abstract string PackageId { get; }

    /// <summary>
    /// Gets the package name containing this interface.
    /// </summary>
    static abstract string PackageName { get; }

    /// <summary>
    /// Gets the package version.
    /// </summary>
    static abstract Version PackageVersion { get; }
}

/// <summary>
/// Interface for Daml interfaces that have an associated view type.
/// </summary>
/// <typeparam name="TView">The view type.</typeparam>
public interface IHasView<TView>
{
    /// <summary>
    /// Gets the interface view.
    /// </summary>
    TView View { get; }
}

/// <summary>
/// Marks a template as implementing a Daml interface.
/// </summary>
/// <typeparam name="TInterface">The interface type.</typeparam>
public interface IImplements<TInterface> where TInterface : IDamlInterface
{
}

/// <summary>
/// Interface for templates that are part of an upgraded package.
/// </summary>
public interface IUpgradeable
{
    /// <summary>
    /// Gets the package ID of the package that this template's package upgrades.
    /// </summary>
    static abstract string? UpgradedPackageId { get; }
}

/// <summary>
/// Provides extension methods for working with Daml templates.
/// </summary>
public static class TemplateExtensions
{
    /// <summary>
    /// Retrieves the fully qualified template identifier for the specified Daml template type.
    /// By default uses the package name format ({packageName}:{moduleName}:{entityName}),
    /// which is compatible with PQS queries. Set <paramref name="usePackageHash"/> to <c>true</c>
    /// to use the package hash format ({packageHash}:{moduleName}:{entityName}) for Ledger API commands.
    /// </summary>
    /// <typeparam name="T">The Daml template type implementing the <see cref="ITemplate"/> interface.</typeparam>
    /// <param name="usePackageHash">
    /// When <c>false</c> (default), returns the package-name-based identifier suitable for PQS queries.
    /// When <c>true</c>, returns the package-hash-based identifier for Ledger API commands.
    /// </param>
    /// <returns>The fully qualified template identifier.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown if the specified template type does not define a valid static <c>TemplateId</c> property.
    /// </exception>
    public static string GetTemplateId<T>(bool usePackageHash = false) where T : ITemplate
    {
        const string templateIdName = nameof(ITemplate.TemplateId);
        var templateIdProperty = typeof(T).GetProperty(
            templateIdName,
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

        if (templateIdProperty?.GetValue(null) is Identifier identifier)
        {
            if (usePackageHash)
            {
                return identifier.ToString();
            }

            var packageNameProperty = typeof(T).GetProperty(
                nameof(ITemplate.PackageName),
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

            if (packageNameProperty?.GetValue(null) is string packageName && !string.IsNullOrEmpty(packageName))
            {
                return $"{packageName}:{identifier.ModuleName}:{identifier.EntityName}";
            }

            // Fallback to hash-based format if PackageName is not available
            return identifier.ToString();
        }

        throw new InvalidOperationException(
            $"Template type '{typeof(T).FullName}' does not have a valid {templateIdName} static property.");
    }

    /// <summary>
    /// Retrieves the fully qualified template identifier for the specified Daml template instance.
    /// By default uses the package name format suitable for PQS queries.
    /// </summary>
    /// <typeparam name="T">The Daml template type implementing the <see cref="ITemplate"/> interface.</typeparam>
    /// <param name="template">The Daml template instance of type <typeparamref name="T"/>.</param>
    /// <param name="usePackageHash">
    /// When <c>false</c> (default), returns the package-name-based identifier.
    /// When <c>true</c>, returns the package-hash-based identifier.
    /// </param>
    /// <returns>The fully qualified template identifier.</returns>
    public static string GetTemplateId<T>(this T template, bool usePackageHash = false) where T : ITemplate
        => GetTemplateId<T>(usePackageHash);
}

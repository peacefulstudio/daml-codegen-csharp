// Copyright (c) 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Data;

namespace Daml.Runtime.Contracts;

/// <summary>
/// Marker interface for all Daml templates.
/// </summary>
/// <remarks>
/// Extends <see cref="IDamlType"/> so that generic helpers which do not require
/// template-specific static metadata can constrain on the broader marker and
/// accept either a concrete template or a Daml interface marker (the sibling
/// <see cref="IDamlInterface"/> defined just below).
/// </remarks>
public interface ITemplate : IDamlRecord, IDamlType
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
/// Marker interface for all Daml interfaces.
/// </summary>
/// <remarks>
/// Extends <see cref="IDamlType"/> as a sibling of <see cref="ITemplate"/>, so
/// generic helpers constrained on the broader marker accept both concrete
/// templates and Daml interface markers without dispatching on template-specific
/// static metadata.
/// </remarks>
public interface IDamlInterface : IDamlRecord, IDamlType
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
/// Selects which fully qualified template identifier format
/// <see cref="TemplateExtensions.GetTemplateId{T}(TemplateIdFormat)"/> produces.
/// </summary>
public enum TemplateIdFormat
{
    /// <summary>
    /// The package-name format (<c>{packageName}:{moduleName}:{entityName}</c>),
    /// expected by read-path consumers: PQS queries and ACS/update filters.
    /// </summary>
    PackageName,

    /// <summary>
    /// The package-hash format (<c>{packageHash}:{moduleName}:{entityName}</c>),
    /// expected by command submission over the Ledger API.
    /// </summary>
    PackageHash,
}

/// <summary>
/// Provides extension methods for working with Daml templates.
/// </summary>
public static class TemplateExtensions
{
    /// <summary>
    /// Retrieves the fully qualified template identifier for the specified Daml template type.
    /// By default uses <see cref="TemplateIdFormat.PackageName"/>
    /// (<c>{packageName}:{moduleName}:{entityName}</c>), which is compatible with PQS queries;
    /// pass <see cref="TemplateIdFormat.PackageHash"/> for Ledger API commands.
    /// </summary>
    /// <typeparam name="T">The Daml template type implementing the <see cref="ITemplate"/> interface.</typeparam>
    /// <param name="format">The identifier format to produce.</param>
    /// <returns>The fully qualified template identifier.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown if <typeparamref name="T"/>'s static <c>PackageName</c> is empty and the
    /// package-name format was requested — a silent fall-back to the hash format would
    /// produce identifiers that match nothing in PQS.
    /// </exception>
    public static string GetTemplateId<T>(TemplateIdFormat format = TemplateIdFormat.PackageName) where T : ITemplate
    {
        var identifier = T.TemplateId;
        if (format == TemplateIdFormat.PackageHash)
        {
            return identifier.ToString();
        }

        if (string.IsNullOrEmpty(T.PackageName))
        {
            throw new InvalidOperationException(
                $"Template type '{typeof(T).FullName}' has an empty static {nameof(ITemplate.PackageName)}; " +
                "cannot build the package-name template identifier. " +
                $"Pass {nameof(TemplateIdFormat)}.{nameof(TemplateIdFormat.PackageHash)} for the package-hash format.");
        }

        return $"{T.PackageName}:{identifier.ModuleName}:{identifier.EntityName}";
    }

    /// <summary>
    /// Retrieves the fully qualified template identifier for the specified Daml template instance.
    /// By default uses <see cref="TemplateIdFormat.PackageName"/>, suitable for PQS queries.
    /// </summary>
    /// <typeparam name="T">The Daml template type implementing the <see cref="ITemplate"/> interface.</typeparam>
    /// <param name="template">The Daml template instance of type <typeparamref name="T"/>.</param>
    /// <param name="format">The identifier format to produce.</param>
    /// <returns>The fully qualified template identifier.</returns>
    public static string GetTemplateId<T>(this T template, TemplateIdFormat format = TemplateIdFormat.PackageName) where T : ITemplate
        => GetTemplateId<T>(format);
}

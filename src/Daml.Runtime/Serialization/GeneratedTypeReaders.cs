// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;

namespace Daml.Runtime.Serialization;

/// <summary>
/// Registry mapping a Daml type's wire identifier to the emitted Daml-LF JSON reader for it.
/// Registration is generic so the compiler binds each type's emitted reader; nothing here reflects
/// over a CLR type's shape. Discovery — deciding which types to register — belongs to the caller and
/// may reflect over an assembly's type list.
/// </summary>
/// <remarks>
/// Every registration is stored under its full identifier (package id, module and entity), never a
/// <see cref="Type"/>. A lookup tries the full identifier first; only if that misses does it fall
/// back to the <c>(ModuleName, EntityName)</c> pair, and that fallback resolves only when exactly one
/// registered declaring type carries the pair — the same rule the ledger's type resolver applies.
/// Keying on module and entity alone would collide whenever two versions of one package are in play,
/// the normal state of a Splice deployment mid-upgrade.
/// </remarks>
/// <threadsafety>
/// All calls to <see cref="ForRecord{T}"/>, <see cref="ForKey{TTemplate, TKey}"/> and
/// <see cref="ForChoices{T}"/> across every caller must complete before the first
/// <c>TryGet*</c> lookup. Registration is explicit application-startup opt-in — there is no
/// module initializer that runs it implicitly — so the ambiguity check a lookup performs over
/// the module/entity fallback (how many distinct declaring types are registered for that pair)
/// is evaluated against a snapshot taken at lookup time. A <c>Register</c> call that lands a
/// second declaring type for the same module/entity pair concurrently with an in-flight lookup
/// can race that snapshot: the lookup may see only the first declaring type and return its
/// entry instead of throwing the ambiguous-lookup exception, even though the registry is (or is
/// about to become) genuinely ambiguous. This type does not synchronize registration with
/// lookups; register all generated types before lookups against this registry have started.
/// </threadsafety>
public static class GeneratedTypeReaders
{
    private static readonly RegistryTable<NoDiscriminator, DamlLfElementReader> Records = new();
    private static readonly RegistryTable<NoDiscriminator, IKeyDescriptor> Keys = new();
    private static readonly RegistryTable<ChoiceName, IChoice> Choices = new();

    /// <summary>Registers the emitted record reader for <typeparamref name="T"/>.</summary>
    public static void ForRecord<T>() where T : IDamlType, IDamlRecord<T>
    {
        var identifier = T.DamlTypeId.Identifier;
        Records.Register(identifier, default, typeof(T), T.__ReadDamlLfJson);
    }

    /// <summary>Registers the emitted key reader of <typeparamref name="TTemplate"/>.</summary>
    public static void ForKey<TTemplate, TKey>()
        where TTemplate : ITemplate, IHasKey<TTemplate, TKey>
    {
        var identifier = TTemplate.TemplateId;
        Keys.Register(identifier, default, typeof(TTemplate), TTemplate.Key);
    }

    /// <summary>Registers every choice descriptor of <typeparamref name="T"/>.</summary>
    public static void ForChoices<T>() where T : IDamlType, IHasChoices<T>
    {
        var identifier = T.DamlTypeId.Identifier;
        foreach (var choice in T.Choices)
        {
            Choices.Register(identifier, choice.Name, typeof(T), choice);
        }
    }

    /// <summary>Finds the emitted record reader registered for a wire identifier.</summary>
    public static bool TryGetRecordReader(Identifier identifier, [MaybeNullWhen(false)] out DamlLfElementReader reader) =>
        Records.TryGet(identifier, default, out reader);

    /// <summary>Finds the registered key descriptor for a wire identifier.</summary>
    public static bool TryGetKeyDescriptor(Identifier identifier, [MaybeNullWhen(false)] out IKeyDescriptor descriptor) =>
        Keys.TryGet(identifier, default, out descriptor);

    /// <summary>Finds the registered choice descriptor for a wire identifier and choice name.</summary>
    public static bool TryGetChoice(Identifier identifier, ChoiceName choice, [MaybeNullWhen(false)] out IChoice descriptor) =>
        Choices.TryGet(identifier, choice, out descriptor);

    private readonly record struct NoDiscriminator;

    private sealed class RegistryTable<TDiscriminator, TValue>
        where TDiscriminator : IEquatable<TDiscriminator>
        where TValue : notnull
    {
        private sealed record Entry(Type DeclaringType, TValue Value);

        private readonly ConcurrentDictionary<(Identifier Identifier, TDiscriminator Discriminator), Entry> _byIdentifier = new();
        private readonly ConcurrentDictionary<(string ModuleName, string EntityName), ConcurrentDictionary<Type, ConcurrentDictionary<TDiscriminator, Entry>>> _byModuleEntity = new();

        public void Register(Identifier identifier, TDiscriminator discriminator, Type declaringType, TValue value)
        {
            var entry = new Entry(declaringType, value);
            _byIdentifier.GetOrAdd((identifier, discriminator), entry);

            var moduleEntityKey = (identifier.ModuleName, identifier.EntityName);
            var byDeclaringType = _byModuleEntity.GetOrAdd(
                moduleEntityKey, _ => new ConcurrentDictionary<Type, ConcurrentDictionary<TDiscriminator, Entry>>());
            var byDiscriminator = byDeclaringType.GetOrAdd(declaringType, _ => new ConcurrentDictionary<TDiscriminator, Entry>());
            byDiscriminator.TryAdd(discriminator, entry);
        }

        public bool TryGet(Identifier identifier, TDiscriminator discriminator, [MaybeNullWhen(false)] out TValue value)
        {
            if (_byIdentifier.TryGetValue((identifier, discriminator), out var exact))
            {
                value = exact.Value;
                return true;
            }

            var moduleEntityKey = (identifier.ModuleName, identifier.EntityName);
            if (_byModuleEntity.TryGetValue(moduleEntityKey, out var byDeclaringType))
            {
                var declaringTypes = byDeclaringType.Keys.ToArray();
                if (declaringTypes.Length > 1)
                {
                    var declaringTypeNames = string.Join(
                        ", ", declaringTypes.Select(candidate => candidate.FullName).OrderBy(name => name, StringComparer.Ordinal));
                    throw new InvalidOperationException(
                        $"Ambiguous Daml-LF JSON reader lookup for module '{identifier.ModuleName}', "
                        + $"entity '{identifier.EntityName}': multiple declaring types are registered "
                        + $"({declaringTypeNames}); pass the full identifier to disambiguate.");
                }

                if (declaringTypes.Length == 1
                    && byDeclaringType[declaringTypes[0]].TryGetValue(discriminator, out var uniqueEntry))
                {
                    value = uniqueEntry.Value;
                    return true;
                }
            }

            value = default;
            return false;
        }
    }
}

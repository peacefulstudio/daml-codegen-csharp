// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.CSharp.Model;

namespace Daml.Codegen.CSharp.CodeGen;

/// <summary>
/// Cardinality of an expected created contract slot in a choice's return type.
/// </summary>
internal enum CreatedCardinality
{
    /// <summary>Single <c>ContractId T</c> — exactly one created contract of <c>T</c> is expected.</summary>
    Single,
    /// <summary>Optional <c>ContractId T</c> — zero or one created contracts of <c>T</c> is expected.</summary>
    Optional,
    /// <summary>List <c>[ContractId T]</c> — any number of created contracts of <c>T</c> is expected.</summary>
    List,
}

/// <summary>
/// The Daml interface a <c>ContractId I</c> slot targets, identified by the
/// <c>(module, entity)</c> pair carried in a created contract's interface ids. Present
/// only when the slot's target is an interface marker; <see langword="null"/> for
/// concrete-template slots.
/// </summary>
/// <remarks>
/// <see cref="ChoiceEmitter"/> checks this record for presence (a non-<see
/// langword="null"/> slot is an interface slot) and then branches on <see
/// cref="IsPlaceholder"/>: for a fully-emitted interface marker, the comparison reads
/// <c>{marker}.InterfaceId.ModuleName</c>/<c>EntityName</c> off the generated marker at
/// runtime, mirroring how the template branch reads <c>{template}.TemplateId</c>. Local
/// interface refs resolve to a C# name whose class shape RecordEmitter alone decides —
/// it always emits the LF-mandated accompanying record as a throwing <c>ITemplate</c>
/// stub with no <c>InterfaceId</c> member (see
/// <c>RecordEmitter.WriteInterfacePlaceholderRecord</c>) — so those stay on the
/// string-literal comparison baked at codegen time instead of trusting a generated
/// symbol that this slot cannot itself confirm exists. <see cref="ModuleName"/> and
/// <see cref="EntityName"/> remain here as the slot's resolved identity for both the
/// literal comparison and testing.
/// </remarks>
/// <param name="ModuleName">The interface's declaring Daml module name.</param>
/// <param name="EntityName">The interface's entity (declaration) name.</param>
/// <param name="IsPlaceholder">
/// <see langword="true"/> when the slot targets a local interface ref — RecordEmitter
/// always emits the local placeholder record as a throwing stub with no
/// <c>InterfaceId</c>, so the projector must match via string literals rather than a
/// generated symbol. <see langword="false"/> for foreign interfaces resolved against
/// another package's own module declarations, which carry no equivalent
/// placeholder-record concept in this package's emission.
/// </param>
internal sealed record InterfaceMatcher(string ModuleName, string EntityName, bool IsPlaceholder);

/// <summary>
/// One declared <c>ContractId T</c>-bearing slot in a choice's return type.
/// </summary>
/// <param name="FieldName">PascalCase C# field name on the emitted <c>&lt;Choice&gt;Result</c> record.</param>
/// <param name="CSharpTemplateType">C# name of the template or interface-marker type (e.g. <c>Agreement</c>, <c>IFactory</c>).</param>
/// <param name="Cardinality">How many created contracts of this template the choice should produce.</param>
/// <param name="Interface">
/// Set when the slot targets a Daml interface marker — generated interface markers expose
/// no <c>TemplateId</c>, so the projector matches an interface slot against the created
/// contract's interface ids rather than its template id.
/// </param>
internal sealed record ChoiceCreatedSlot(
    string FieldName,
    string CSharpTemplateType,
    CreatedCardinality Cardinality,
    InterfaceMatcher? Interface = null);

/// <summary>
/// Walks a choice's return type for embedded <c>ContractId T</c> references and returns
/// one slot per reference. Pure: a return type and the per-package resolution inputs go
/// in, a list of slots comes out, with no emitter state. Unit-testable directly without
/// emitting any source.
/// </summary>
internal static class ChoiceCreatedSlots
{
    /// <summary>
    /// Walks the choice's return type for embedded <c>ContractId T</c> references and
    /// returns one slot per reference (preserving declaration order). Returns an empty
    /// list when the return type carries no contract IDs — those choices don't get a
    /// <c>&lt;Choice&gt;Result</c> emitted.
    /// </summary>
    /// <remarks>
    /// <para>
    ///   Recognised return-type shapes:
    ///   <list type="bullet">
    ///     <item><c>ContractId T</c> — single-create.</item>
    ///     <item><c>Optional (ContractId T)</c> — optional-create.</item>
    ///     <item><c>[ContractId T]</c> — list-create.</item>
    ///     <item><c>(ContractId A, ContractId B, ...)</c> — Daml tuples (LF: <c>DA.Types:Tuple{N}</c>) — flattened across components.</item>
    ///   </list>
    /// </para>
    /// <para>
    ///   Anything else (records, primitives, plain <c>Unit</c>) yields an empty list —
    ///   the choice is treated as non-creating from the codegen's perspective. This
    ///   intentionally undershoots: a choice whose body creates contracts but returns
    ///   <c>Unit</c> won't get a typed projector. Consumers can fall back to walking
    ///   <c>tx.CreatedContracts</c> manually for those cases.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<ChoiceCreatedSlot> Extract(PackageEmitContext context, ICrossPackageResolver resolver, DamlTypeMapper mapper, DamlType returnType)
    {
        var slots = new List<ChoiceCreatedSlot>();
        Walk(context, resolver, mapper, returnType, slots, parentCardinality: CreatedCardinality.Single);
        return Disambiguate(slots);
    }

    private static IReadOnlyList<ChoiceCreatedSlot> Disambiguate(List<ChoiceCreatedSlot> slots)
    {
        var taken = new HashSet<string>(slots.Select(slot => slot.FieldName), StringComparer.Ordinal);
        var assigned = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < slots.Count; i++)
        {
            var baseName = slots[i].FieldName;
            if (assigned.Add(baseName))
            {
                continue;
            }
            var suffix = 2;
            string candidate;
            do
            {
                candidate = $"{baseName}{suffix}";
                suffix++;
            }
            while (taken.Contains(candidate) || !assigned.Add(candidate));
            slots[i] = slots[i] with { FieldName = candidate };
        }
        return slots;
    }

    private static void Walk(
        PackageEmitContext context, ICrossPackageResolver resolver, DamlTypeMapper mapper,
        DamlType type,
        List<ChoiceCreatedSlot> slots,
        CreatedCardinality parentCardinality)
    {
        switch (type)
        {
            // ContractId T — single-create slot. Inherit `parentCardinality` from any
            // wrapping Optional/List so a `[ContractId T]` becomes a List slot rather
            // than a Single one.
            case DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.ContractId }, Arguments: [var arg] }:
            {
                var (templateName, csharpName, interfaceMatcher) = ResolveContractIdTarget(context, resolver, mapper, arg);
                slots.Add(new ChoiceCreatedSlot(
                    FieldName: templateName,
                    CSharpTemplateType: csharpName,
                    Cardinality: parentCardinality,
                    Interface: interfaceMatcher));
                return;
            }
            // Optional (ContractId T) — recurse with Optional cardinality.
            case DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.Optional }, Arguments: [var inner] }:
                Walk(context, resolver, mapper, inner, slots, CreatedCardinality.Optional);
                return;
            // [ContractId T] — recurse with List cardinality.
            case DamlTypeApp { Base: DamlPrimitiveType { Primitive: DamlPrimitive.List }, Arguments: [var inner] }:
                Walk(context, resolver, mapper, inner, slots, CreatedCardinality.List);
                return;
            // Daml tuple (DA.Types:Tuple2/3/.../N) — flatten over the components.
            case DamlTypeApp { Base: DamlTypeRef { Module: "DA.Types", Name: var tupleName } } app
                when tupleName.StartsWith("Tuple", StringComparison.Ordinal):
                for (var i = 0; i < app.Arguments.Count; i++)
                {
                    Walk(context, resolver, mapper, app.Arguments[i], slots, parentCardinality);
                }
                return;
            default:
                // Records, variants, primitives, type vars, plain Unit — no created-slot
                // contribution. Codegen treats these as non-creating return types.
                return;
        }
    }

    private static (string FieldName, string CSharpTemplateType, InterfaceMatcher? Interface) ResolveContractIdTarget(PackageEmitContext context, ICrossPackageResolver resolver, DamlTypeMapper mapper, DamlType arg)
    {
        switch (arg)
        {
            case DamlTypeRef typeRef:
            {
                var fieldName = Identifiers.Sanitize(typeRef.Name);
                var csharpName = resolver.Resolve(typeRef, context);
                return (fieldName, csharpName, ResolveInterfaceMatcher(context, resolver, typeRef));
            }
            case DamlTypeApp { Base: DamlTypeRef typeRef }:
            {
                var fieldName = Identifiers.Sanitize(typeRef.Name);
                var csharpName = mapper.MapType(arg);
                return (fieldName, csharpName, ResolveInterfaceMatcher(context, resolver, typeRef));
            }
            default:
                // Type variable or otherwise opaque target — fall back to the mapped C#
                // name and a synthetic field name. Generated code may not compile in this
                // case; callers will see a clear loud failure at consumer build time.
                var mapped = mapper.MapType(arg);
                return ("Created", mapped, null);
        }
    }

    // Mirrors the interface-marker branches of DarCrossPackageResolver.Resolve: a slot
    // targets a Daml interface exactly when the resolver would emit an interface-marker
    // name for it. Local interfaces live in the placeholder set; foreign interfaces are
    // read from the referenced package's interface declarations.
    private static InterfaceMatcher? ResolveInterfaceMatcher(PackageEmitContext context, ICrossPackageResolver resolver, DamlTypeRef typeRef)
    {
        if (context.IsLocalRef(typeRef))
        {
            var isLocalInterface = context.InterfacePlaceholderQualifiedNames.Contains($"{typeRef.Module}:{typeRef.Name}");
            return isLocalInterface ? new InterfaceMatcher(typeRef.Module, typeRef.Name, IsPlaceholder: true) : null;
        }

        var isForeignInterface = resolver.LookupPackage(typeRef.PackageId) is { } pkg
            && !StdlibPackages.IsStdlibPackage(pkg.Name)
            && !StdlibPackages.IsPlaceholderPackageName(pkg.Name)
            && pkg.Modules.Any(module => module.Name == typeRef.Module
                && module.Interfaces.Any(iface => iface.Name == typeRef.Name));

        return isForeignInterface ? new InterfaceMatcher(typeRef.Module, typeRef.Name, IsPlaceholder: false) : null;
    }
}

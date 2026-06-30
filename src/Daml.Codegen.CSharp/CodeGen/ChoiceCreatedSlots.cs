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
/// One declared <c>ContractId T</c>-bearing slot in a choice's return type.
/// </summary>
/// <param name="FieldName">PascalCase C# field name on the emitted <c>&lt;Choice&gt;Result</c> record.</param>
/// <param name="CSharpTemplateType">C# name of the template type (e.g. <c>Agreement</c>, <c>SwapRecord</c>).</param>
/// <param name="Cardinality">How many created contracts of this template the choice should produce.</param>
internal sealed record ChoiceCreatedSlot(
    string FieldName,
    string CSharpTemplateType,
    CreatedCardinality Cardinality);

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
                var (templateName, csharpName) = ResolveContractIdTarget(context, resolver, mapper, arg);
                slots.Add(new ChoiceCreatedSlot(
                    FieldName: templateName,
                    CSharpTemplateType: csharpName,
                    Cardinality: parentCardinality));
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

    private static (string FieldName, string CSharpTemplateType) ResolveContractIdTarget(PackageEmitContext context, ICrossPackageResolver resolver, DamlTypeMapper mapper, DamlType arg)
    {
        switch (arg)
        {
            case DamlTypeRef typeRef:
            {
                var fieldName = Identifiers.Sanitize(typeRef.Name);
                var csharpName = resolver.Resolve(typeRef, context);
                return (fieldName, csharpName);
            }
            case DamlTypeApp { Base: DamlTypeRef typeRef }:
            {
                var fieldName = Identifiers.Sanitize(typeRef.Name);
                var csharpName = mapper.MapType(arg);
                return (fieldName, csharpName);
            }
            default:
                // Type variable or otherwise opaque target — fall back to the mapped C#
                // name and a synthetic field name. Generated code may not compile in this
                // case; callers will see a clear loud failure at consumer build time.
                var mapped = mapper.MapType(arg);
                return ("Created", mapped);
        }
    }
}

// Copyright (c) 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Codegen.CSharp.Model;

namespace Daml.Codegen.CSharp.CodeGen;

/// <summary>
/// Pure reasoning about a template's parties — classifying controller / signatory /
/// observer sets as statically-resolvable or <see cref="DamlPartySource.Dynamic"/>,
/// unioning two analyses, partitioning parties into controller-params and
/// observer-only-params, and validating a <see cref="DamlPartyAnalysis"/> against the
/// template's actual fields. Party sets in, classified / partitioned params out — no
/// emitter state and no writer — so the choice and submission emitters can share it
/// without duplicating the logic. Shared dependency of the choice-exerciser and
/// submission-extension emitters.
/// </summary>
public sealed class PartyAnalysis
{
    /// <summary>
    /// Computes the effective <c>readAs</c> set for a choice as the union of two
    /// party analyses, preserving declaration order and deduplicating by payload
    /// field name. Returns a <see cref="DamlPartySource.Static"/> result only when
    /// both inputs are static and every reference is a <see cref="DamlPartyPayloadField"/>;
    /// a <see cref="DamlPartySource.Dynamic"/> verdict on either side — or a non-payload-field
    /// reference carried by an otherwise-static input — is contagious, since the union of a
    /// known set with an unknown one is unknown.
    /// </summary>
    public DamlPartyAnalysis UnionStaticParties(DamlPartyAnalysis a, DamlPartyAnalysis b)
    {
        if (a.Source != DamlPartySource.Static || b.Source != DamlPartySource.Static)
        {
            return DamlPartyAnalysis.Dynamic;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var union = new List<DamlPartyReference>();
        foreach (var p in a.Parties.Concat(b.Parties))
        {
            if (p is not DamlPartyPayloadField pf)
            {
                return DamlPartyAnalysis.Dynamic;
            }
            if (seen.Add(pf.FieldName))
            {
                union.Add(p);
            }
        }
        return DamlPartyAnalysis.Static(union);
    }

    /// <summary>
    /// Splits analyzed controllers and observers into two ordered lists of camelCased
    /// <c>Party</c> parameter names: one for controllers (which feed
    /// <c>SubmitterInfo.actAs</c>) and one for observer-only parties (which feed
    /// <c>SubmitterInfo.readAs</c>). Observer parties that are also controllers are NOT
    /// duplicated in the readAs list — deduplication is by payload-field name, mirroring
    /// the Daml semantics. The readAs list is empty when <paramref name="observers"/> is
    /// dynamic or empty — only statically-resolved analyses contribute params.
    /// </summary>
    /// <returns><c>(controllerParams, readAsParams)</c>, both in declaration order.</returns>
    /// <exception cref="InvalidOperationException">
    /// A statically-resolved analysis carries a non-<see cref="DamlPartyPayloadField"/>
    /// reference; callers must pre-validate via <c>ValidatePayloadParties</c> /
    /// <c>UnionStaticParties</c>, which demote unknown subtypes to
    /// <see cref="DamlPartySource.Dynamic"/>.
    /// </exception>
    public (List<string> controllerParams, List<string> readAsParams)
        PartitionControllersAndObservers(DamlPartyAnalysis controllers, DamlPartyAnalysis observers)
    {
        var controllerParams = new List<string>();
        var controllerFieldNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var pf in PayloadFieldsOf(controllers))
        {
            if (controllerFieldNames.Add(pf.FieldName))
            {
                controllerParams.Add(ToCamelCaseParam(pf.FieldName));
            }
        }

        var readAsParams = new List<string>();
        var seenObservers = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pf in PayloadFieldsOf(observers))
        {
            if (!controllerFieldNames.Contains(pf.FieldName) && seenObservers.Add(pf.FieldName))
            {
                readAsParams.Add(ToCamelCaseParam(pf.FieldName));
            }
        }

        return (controllerParams, readAsParams);
    }

    private static IEnumerable<DamlPartyPayloadField> PayloadFieldsOf(DamlPartyAnalysis analysis)
    {
        if (analysis.Source != DamlPartySource.Static)
        {
            yield break;
        }

        foreach (var p in analysis.Parties)
        {
            if (p is not DamlPartyPayloadField pf)
            {
                throw new InvalidOperationException(
                    $"Static party analysis carries a {p.GetType().Name} reference; partition requires "
                    + "callers to pre-validate via ValidatePayloadParties/UnionStaticParties, which demote "
                    + "unknown subtypes to Dynamic.");
            }
            yield return pf;
        }
    }

    /// <summary>
    /// Re-validates a static party analysis against the actual payload fields. Demotes to
    /// <see cref="DamlPartySource.Dynamic"/> when any reference is not a
    /// <see cref="DamlPartyPayloadField"/>, or claims <c>payload.X</c> but the template has
    /// no <c>Party</c>-typed field named <c>X</c> — better to ask the caller for an explicit
    /// submitter than to emit code that won't compile. Dynamic and static-empty analyses
    /// pass through unchanged.
    /// </summary>
    public DamlPartyAnalysis ValidatePayloadParties(
        DamlPartyAnalysis analysis,
        IReadOnlyDictionary<string, DamlFieldDefinition> partyFields)
    {
        if (analysis.Source != DamlPartySource.Static)
        {
            return analysis;
        }

        foreach (var p in analysis.Parties)
        {
            if (p is not DamlPartyPayloadField payloadField
                || !partyFields.ContainsKey(payloadField.FieldName))
            {
                return DamlPartyAnalysis.Dynamic;
            }
        }

        return analysis;
    }

    private static string ToCamelCaseParam(string name)
    {
        var sanitized = Identifiers.Sanitize(name);
        if (string.IsNullOrEmpty(sanitized))
        {
            return sanitized;
        }
        if (char.IsUpper(sanitized[0]))
        {
            return char.ToLowerInvariant(sanitized[0]) + sanitized[1..];
        }
        return sanitized;
    }
}

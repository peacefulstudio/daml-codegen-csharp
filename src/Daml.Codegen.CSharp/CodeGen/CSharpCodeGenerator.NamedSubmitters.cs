// Copyright (c) 2026 Peaceful Studio OÜ. All rights reserved.

using Daml.Codegen.CSharp.Model;

namespace Daml.Codegen.CSharp.CodeGen;

/// <summary>
/// Codegen for the typed-submitter surface — issue #68. Each template gets a
/// <c>&lt;TemplateName&gt;SubmissionExtensions</c> static class with:
///
/// <list type="bullet">
///   <item>One <c>CreateAsync</c> overload that takes the template payload (and
///   any extra parties that the analyzer flagged as not derivable from the
///   payload) and submits a Create command via
///   <c>ILedgerClient.TryCreateAsync</c>. When every signatory is a payload
///   field reference (<c>signatory platform, initiator, counterparty</c> with
///   matching <c>Party</c> fields), the wrapper picks them off the payload
///   automatically — the caller never restates a party that's already in the
///   record.</item>
///   <item>An <c>Observers(payload)</c> documentation helper, when the
///   template's <c>observer</c> clause is statically resolvable. Returns the
///   ordered list of <see cref="Daml.Runtime.Data.Party"/> derived from the
///   payload — useful for inspecting / asserting the observer set without
///   exercising a choice.</item>
/// </list>
///
/// <para>
/// The typed-controller <c>&lt;Choice&gt;Async</c> wrappers are emitted in a
/// sibling <c>&lt;TemplateName&gt;Extensions</c> class — see
/// <c>CSharpCodeGenerator.ChoiceResults.cs</c>. They thread the payload-derived
/// <c>actAs</c> parties (from the choice's controllers) and the payload-derived
/// <c>readAs</c> parties (from the template-level and choice-level observers,
/// deduplicated) into a <see cref="Daml.Runtime.Commands.SubmitterInfo"/> that
/// reaches the ledger via <c>ILedgerClient.TrySubmitAndWaitForTransactionAsync</c>.
/// </para>
///
/// <para>
/// When the static analyzer cannot resolve a <c>signatory</c> /
/// <c>controller</c> expression (anything other than a payload-field
/// projection on the template parameter), codegen falls back to a single
/// <c>SubmitterInfo submitter</c> parameter — callers retain full control via
/// <c>SubmitterInfo</c>'s implicit conversion from <c>string</c> /
/// <c>Party</c>, so the legacy single-party call site stays a one-liner.
/// </para>
/// </summary>
public sealed partial class CSharpCodeGenerator
{
    /// <summary>
    /// Emits the per-template <c>SubmissionExtensions</c> static class. Returns
    /// <c>true</c> when the class was emitted (we always emit it for templates
    /// that have at least one choice or whose Create surface could benefit
    /// from payload-derived parties). The caller doesn't actually use the
    /// return value today; it's kept for symmetry with sibling emitters.
    /// </summary>
    private bool TryWriteNamedSubmitterExtensions(
        IndentWriter indent,
        DamlTemplate template,
        IReadOnlyList<DamlField> fields,
        IReadOnlyDictionary<string, DamlDataType> dataTypes)
    {
        var className = SanitizeIdentifier(template.Name);

        // Build the per-field type map so we can validate that every
        // payload-derived signatory really points at a Party-typed field. A
        // mismatch (analyzer claimed payload field but the field doesn't exist
        // or isn't a Party) means the DAR shape is unexpected and we should
        // fall back to Dynamic rather than emit an uncompilable reference.
        var partyFields = fields
            .Where(f => f.Type is DamlPrimitiveType { Primitive: DamlPrimitive.Party })
            .ToDictionary(f => f.Name, f => f, StringComparer.Ordinal);

        // Validate signatories — if any payload-derived field doesn't exist
        // (or isn't a Party), demote to Dynamic.
        var signatories = ValidatePayloadParties(template.Signatories, partyFields);

        // Validate template-level observers the same way. A static-empty
        // analysis (the literal `[]` Daml expression) stays Static — that
        // signals "no observers, by design" and tells codegen to skip the
        // helper but still treat the readAs contribution at choice exercise
        // as a known-empty set.
        var observers = ValidatePayloadParties(template.Observers, partyFields);

        RequireAsyncExerciserNamespaces(indent);

        indent.AppendLine();
        if (options.GenerateXmlDocs)
        {
            indent.AppendLine("/// <summary>");
            indent.AppendLine($"/// Typed-submitter extensions for <see cref=\"{className}\"/>. The");
            indent.AppendLine("/// <see cref=\"CreateAsync\"/> overload derives the <c>actAs</c> parties");
            indent.AppendLine("/// from the template payload (for signatories that are payload-field");
            indent.AppendLine("/// references) and from the caller's explicit arguments (for signatories");
            indent.AppendLine("/// the static analyzer could not resolve). When the template's");
            indent.AppendLine("/// <c>observer</c> clause is statically resolvable, an");
            indent.AppendLine("/// <c>Observers(payload)</c> documentation helper is also emitted.");
            indent.AppendLine("/// Closes peacefulstudio/daml-codegen-csharp#68.");
            indent.AppendLine("/// </summary>");
        }
        indent.AppendLine($"public static class {className}SubmissionExtensions");
        indent.AppendLine("{");
        indent.Indent();

        WriteCreateAsync(indent, className, signatories);
        TryWriteObserversHelper(indent, className, observers);

        indent.Dedent();
        indent.AppendLine("}");

        return true;
    }

    /// <summary>
    /// Emits the per-template <c>Observers(payload)</c> documentation helper
    /// when the template's <c>observer</c> clause resolves statically to a
    /// non-empty set of payload-field references. Returns an
    /// <see cref="IReadOnlyList{Party}"/> derived from the payload's
    /// PascalCased <c>Party</c> properties so callers can inspect the observer
    /// set directly.
    ///
    /// <para>
    /// Skipped when the analyzer's verdict is <see cref="DamlPartySource.Dynamic"/>
    /// (we can't know the answer at codegen time) or when it's a static empty
    /// list (the helper would always return <c>[]</c> — emitting it would be
    /// noise). Static-empty observers still register as a known-empty
    /// contribution to <c>readAs</c> on the emitted <c>&lt;Choice&gt;Async</c>
    /// wrappers, which is handled in <c>ChoiceResults.cs</c>.
    /// </para>
    /// </summary>
    private void TryWriteObserversHelper(IndentWriter indent, string className, DamlPartyAnalysis observers)
    {
        // Skip dynamic and static-empty cases per the doc comment above.
        if (observers.Source != DamlPartySource.Static || observers.Parties.Count == 0)
        {
            return;
        }

        indent.Require("System.Collections.Generic");
        indent.AppendLine();
        if (options.GenerateXmlDocs)
        {
            indent.AppendLine("/// <summary>");
            indent.AppendLine($"/// Returns the observers of a <see cref=\"{className}\"/> payload —");
            indent.AppendLine("/// the parties named in the Daml <c>observer</c> clause, derived from");
            indent.AppendLine("/// the payload's <c>Party</c> properties in declaration order. Useful");
            indent.AppendLine("/// for inspecting / asserting the observer set without exercising a");
            indent.AppendLine("/// choice. The same set is contributed to <c>SubmitterInfo.readAs</c>");
            indent.AppendLine("/// on the emitted <c>&lt;Choice&gt;Async</c> wrappers.");
            indent.AppendLine("/// </summary>");
            indent.AppendLine("/// <param name=\"payload\">The contract payload.</param>");
        }

        indent.AppendLine($"public static {_qualifier.Qualify("IReadOnlyList", _currentNamespace)}<global::Daml.Runtime.Data.Party> Observers({className} payload)");
        indent.AppendLine("{");
        indent.Indent();
        indent.AppendLine("ArgumentNullException.ThrowIfNull(payload);");
        indent.AppendLine("return new global::Daml.Runtime.Data.Party[]");
        indent.AppendLine("{");
        indent.Indent();
        for (var i = 0; i < observers.Parties.Count; i++)
        {
            if (observers.Parties[i] is DamlPartyPayloadField pf)
            {
                var prop = ToPascalCase(SanitizeIdentifier(pf.FieldName));
                var comma = i < observers.Parties.Count - 1 ? "," : "";
                indent.AppendLine($"payload.{prop}{comma}");
            }
        }
        indent.Dedent();
        indent.AppendLine("};");
        indent.Dedent();
        indent.AppendLine("}");
    }

    /// <summary>
    /// Re-validates a static party analysis against the actual payload fields.
    /// If the analyzer claimed <c>payload.X</c> but the template has no
    /// <c>Party</c>-typed field named <c>X</c>, demote to
    /// <see cref="DamlPartySource.Dynamic"/> — better to ask the caller for
    /// an explicit submitter than to emit code that won't compile.
    /// </summary>
    private static DamlPartyAnalysis ValidatePayloadParties(
        DamlPartyAnalysis analysis,
        IReadOnlyDictionary<string, DamlField> partyFields)
    {
        if (analysis.Source != DamlPartySource.Static)
        {
            return analysis;
        }

        foreach (var p in analysis.Parties)
        {
            if (p is DamlPartyPayloadField payloadField
                && !partyFields.ContainsKey(payloadField.FieldName))
            {
                return DamlPartyAnalysis.Dynamic;
            }
        }

        return analysis;
    }

    /// <summary>
    /// Emits the <c>CreateAsync</c> extension. On the static path, the payload
    /// alone is enough to derive every signatory — the caller passes only the
    /// payload and an optional cancellation token, and the wrapper builds a
    /// <c>SubmitterInfo</c> from the payload's PascalCased <c>Party</c>
    /// properties. On the dynamic path, the caller passes a
    /// <c>SubmitterInfo</c> directly (with implicit conversion from
    /// <c>string</c> / <c>Party</c> for the single-party ergonomic).
    /// </summary>
    private void WriteCreateAsync(IndentWriter indent, string className, DamlPartyAnalysis signatories)
    {
        var staticParties = signatories.Source == DamlPartySource.Static
                            && signatories.Parties.Count > 0;
        var multipleStatic = staticParties && signatories.Parties.Count > 1;

        if (multipleStatic)
        {
            indent.Require("System.Collections.Generic");
        }

        if (options.GenerateXmlDocs)
        {
            indent.AppendLine("/// <summary>");
            indent.AppendLine($"/// Creates a new <see cref=\"{className}\"/> contract on the ledger.");
            if (staticParties)
            {
                indent.AppendLine("/// The submitting parties are derived from the payload — each Daml signatory is");
                indent.AppendLine("/// a reference to a payload field, so the caller never restates a party that's");
                indent.AppendLine("/// already in the record.");
            }
            else
            {
                indent.AppendLine("/// The submitter is passed explicitly via <paramref name=\"submitter\"/>. The static");
                indent.AppendLine("/// analyzer could not resolve the Daml <c>signatory</c> clause to payload-field");
                indent.AppendLine("/// references — typically because the expression involves the template key, a");
                indent.AppendLine("/// constant, or a function call. <see cref=\"SubmitterInfo\"/> implicitly converts");
                indent.AppendLine("/// from <c>string</c> / <c>Party</c>, so single-party callers still pass one literal.");
            }
            indent.AppendLine("/// </summary>");
            indent.AppendLine("/// <param name=\"client\">The ledger client.</param>");
            indent.AppendLine("/// <param name=\"payload\">The contract payload.</param>");
            if (!staticParties)
            {
                indent.AppendLine("/// <param name=\"submitter\">The submitter party set (<c>actAs</c> + optional <c>readAs</c>).</param>");
            }
            indent.AppendLine("/// <param name=\"cancellationToken\">Cancellation token.</param>");
        }

        indent.AppendLine($"public static Task<{_qualifier.Qualify("ExerciseOutcome", _currentNamespace)}<{_qualifier.Qualify("ContractId", _currentNamespace)}<{className}>>> CreateAsync(");
        indent.Indent();
        indent.AppendLine($"this {_qualifier.Qualify("ILedgerClient", _currentNamespace)} client,");
        indent.AppendLine($"{className} payload,");
        if (!staticParties)
        {
            indent.AppendLine($"{_qualifier.Qualify("SubmitterInfo", _currentNamespace)} submitter,");
        }
        indent.AppendLine("CancellationToken cancellationToken = default)");
        indent.Dedent();

        indent.AppendLine("{");
        indent.Indent();

        indent.AppendLine("ArgumentNullException.ThrowIfNull(client);");
        indent.AppendLine("ArgumentNullException.ThrowIfNull(payload);");
        indent.AppendLine();

        if (staticParties)
        {
            indent.AppendLine("// Each Daml signatory is a payload field; the wrapper builds a SubmitterInfo");
            indent.AppendLine("// from those Party properties so the caller never restates a party.");
            if (multipleStatic)
            {
                indent.AppendLine($"var submitter = new {_qualifier.Qualify("SubmitterInfo", _currentNamespace)}(new {_qualifier.Qualify("HashSet", _currentNamespace)}<global::Daml.Runtime.Data.Party>");
                indent.AppendLine("{");
                indent.Indent();
                for (var i = 0; i < signatories.Parties.Count; i++)
                {
                    if (signatories.Parties[i] is DamlPartyPayloadField pf)
                    {
                        var prop = ToPascalCase(SanitizeIdentifier(pf.FieldName));
                        var comma = i < signatories.Parties.Count - 1 ? "," : "";
                        indent.AppendLine($"payload.{prop}{comma}");
                    }
                }
                indent.Dedent();
                indent.AppendLine("});");
            }
            else
            {
                // Single-party static: pass the Party directly, relying on the
                // implicit conversion to SubmitterInfo. Avoids the HashSet allocation.
                var pf = (DamlPartyPayloadField)signatories.Parties[0];
                var prop = ToPascalCase(SanitizeIdentifier(pf.FieldName));
                indent.AppendLine($"{_qualifier.Qualify("SubmitterInfo", _currentNamespace)} submitter = payload.{prop};");
            }
            indent.AppendLine();
        }

        indent.AppendLine($"return client.TryCreateAsync<{className}>(payload, submitter, cancellationToken: cancellationToken);");

        indent.Dedent();
        indent.AppendLine("}");
    }

    /// <summary>
    /// Lower-cases the first character of a sanitized identifier (to produce a
    /// camelCase parameter name from a Daml field name). Daml field names are
    /// camelCase by convention; we sanitize-and-recase defensively in case
    /// SanitizeIdentifier upper-cased the first letter (e.g. for an
    /// underscore-prefixed name). Used by the typed-controller
    /// <c>&lt;Choice&gt;Async</c> emitter in <c>ChoiceResults.cs</c>.
    /// </summary>
    private static string ToCamelCaseParam(string name)
    {
        var sanitized = SanitizeIdentifier(name);
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

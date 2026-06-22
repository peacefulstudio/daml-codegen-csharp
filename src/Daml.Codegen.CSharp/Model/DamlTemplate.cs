// Copyright (c) 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Daml.Codegen.CSharp.Model;

/// <summary>
/// Represents a Daml template.
/// </summary>
public sealed class DamlTemplate
{
    /// <summary>
    /// Gets the template name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the template fields.
    /// </summary>
    public required IReadOnlyList<DamlFieldDefinition> Fields { get; init; }

    /// <summary>
    /// Gets the choices defined on this template.
    /// </summary>
    public required IReadOnlyList<DamlChoice> Choices { get; init; }

    /// <summary>
    /// Gets the key type, if this template has a key.
    /// </summary>
    public DamlType? Key { get; init; }

    /// <summary>
    /// Gets the interfaces this template implements.
    /// </summary>
    public IReadOnlyList<string> Implements { get; init; } = [];

    /// <summary>
    /// Gets the static signatory analysis for this template — the parsed shape
    /// of the Daml <c>signatory ...</c> clause. <see cref="DamlPartySource.Static"/>
    /// when every signatory resolves to a <see cref="DamlPartyPayloadField"/>
    /// (a record-field projection on the template parameter), or to an empty
    /// list; <see cref="DamlPartySource.Dynamic"/> when the expression involves
    /// anything else (e.g. function calls, projection through nested records,
    /// references to a `key` value, constants, etc.).
    /// Codegen uses this to decide whether <c>CreateAsync</c> derives <c>actAs</c>
    /// from <c>payload</c> automatically (Static path) or requires the caller to
    /// pass <c>SubmitterInfo</c> explicitly (Dynamic path).
    /// </summary>
    public DamlPartyAnalysis Signatories { get; init; } = DamlPartyAnalysis.Dynamic;

    /// <summary>
    /// Gets the static observer analysis for this template — the parsed shape
    /// of the Daml <c>observer ...</c> clause. Same shape semantics as
    /// <see cref="Signatories"/>. When statically resolvable, codegen emits an
    /// <c>Observers(payload)</c> documentation helper on the per-template
    /// <c>SubmissionExtensions</c> class and contributes the observer parties
    /// to <c>SubmitterInfo.readAs</c> at choice exercise so the wire format
    /// carries the right read-as set for stakeholder visibility.
    /// </summary>
    public DamlPartyAnalysis Observers { get; init; } = DamlPartyAnalysis.Dynamic;
}

/// <summary>
/// Represents a choice on a template.
/// </summary>
public sealed class DamlChoice
{
    /// <summary>
    /// Gets the choice name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets whether this choice consumes the contract.
    /// </summary>
    public required bool Consuming { get; init; }

    /// <summary>
    /// Gets the choice argument type.
    /// </summary>
    public required DamlType ArgumentType { get; init; }

    /// <summary>
    /// Gets the choice return type.
    /// </summary>
    public required DamlType ReturnType { get; init; }

    /// <summary>
    /// Gets the static controller analysis for this choice — see
    /// <see cref="DamlTemplate.Signatories"/> for the shape semantics. Controllers
    /// can additionally reference the choice argument record (the <c>self</c>
    /// binder), which is currently treated as <see cref="DamlPartySource.Dynamic"/>
    /// because the codegen would need to invent a name for the argument value.
    /// </summary>
    public DamlPartyAnalysis Controllers { get; init; } = DamlPartyAnalysis.Dynamic;

    /// <summary>
    /// Gets the static observer analysis for this choice — the parsed shape
    /// of the Daml choice-level <c>observer ...</c> clause (the
    /// <c>ChoiceObserver</c> field on the LF expression form). Same shape
    /// semantics as <see cref="DamlTemplate.Signatories"/>. Codegen unions the
    /// resolved observer parties with the template-level observers and
    /// contributes the result to <c>SubmitterInfo.readAs</c> on the emitted
    /// <c>&lt;Choice&gt;Async</c> wrapper so visibility propagates to the
    /// command submission.
    /// </summary>
    public DamlPartyAnalysis Observers { get; init; } = DamlPartyAnalysis.Dynamic;
}

/// <summary>
/// Outcome of statically analyzing a Daml party expression
/// (<c>signatory ...</c>, <c>controller ...</c>). The analyzer either resolves
/// the expression to a deterministic, well-known set of party sources (each one
/// is a payload-field reference) or gives up and surfaces a
/// <see cref="DamlPartySource.Dynamic"/> marker, in which case codegen falls back
/// to the explicit <c>SubmitterInfo</c> shape.
/// </summary>
/// <param name="Source">Static vs. dynamic — see the type docs.</param>
/// <param name="Parties">When <see cref="Source"/> is
/// <see cref="DamlPartySource.Static"/>, the ordered list of payload-derived
/// parties (declaration order from the Daml source). Empty when dynamic.</param>
public sealed record DamlPartyAnalysis(DamlPartySource Source, IReadOnlyList<DamlPartyReference> Parties)
{
    /// <summary>The fallback when the expression cannot be statically resolved.</summary>
    public static readonly DamlPartyAnalysis Dynamic = new(DamlPartySource.Dynamic, []);

    /// <summary>Convenience constructor for the static case.</summary>
    public static DamlPartyAnalysis Static(IReadOnlyList<DamlPartyReference> parties) =>
        new(DamlPartySource.Static, parties);
}

/// <summary>Distinguishes resolvable party expressions from opaque ones.</summary>
public enum DamlPartySource
{
    /// <summary>Resolved statically — every party is a known payload-field reference.</summary>
    Static,

    /// <summary>Could not be resolved — codegen must accept an explicit submitter list.</summary>
    Dynamic,
}

/// <summary>
/// A single party-valued expression resolved by the analyzer. Currently the only
/// supported shape is <c>PayloadField</c> — a reference to a <c>Party</c>-typed
/// field on the template payload. Future shapes (constants, nested-record
/// projection, key-derived parties) live behind their own subclasses so the
/// codegen can pattern-match them explicitly.
/// </summary>
public abstract record DamlPartyReference;

/// <summary>
/// A reference to a <c>Party</c>-typed field on the template payload, e.g.
/// <c>signatory platform</c> when the template has <c>platform : Party</c>. The
/// generated <c>CreateAsync</c> derives <c>actAs</c> from
/// <c>payload.&lt;FieldName&gt;</c> rather than asking the caller to pass it
/// again.
/// </summary>
/// <param name="FieldName">The Daml field name (lower-camel-case).</param>
public sealed record DamlPartyPayloadField(string FieldName) : DamlPartyReference;

/// <summary>
/// Represents a Daml interface.
/// </summary>
public sealed class DamlInterface
{
    /// <summary>
    /// Gets the interface name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the choices exposed by this interface. (The Daml-LF
    /// <c>InterfaceMethod</c> list — distinct from interface choices —
    /// is not yet plumbed through the C# emitter; see PR #168 deferred
    /// divergences.)
    /// </summary>
    public required IReadOnlyList<DamlChoice> Choices { get; init; }

    /// <summary>
    /// Gets the view type for this interface.
    /// </summary>
    public DamlType? ViewType { get; init; }
}

// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Daml.Runtime.Streams;

/// <summary>
/// Why an event delivered on a subscription stream could not be mapped to a typed
/// <see cref="ContractStreamEvent{T}"/> variant and was surfaced as
/// <see cref="ContractStreamEvent{T}.Unclassified"/>. Consumers <c>switch</c> on this
/// discriminator instead of matching magic strings, so the compiler catches a missing
/// or misspelled arm.
/// </summary>
/// <remarks>
/// <see cref="Unknown"/> is the forward-compatible escape hatch: a transport that
/// delivers an event this enum has no member for classifies it as <see cref="Unknown"/>
/// and carries the transport's raw descriptor on
/// <see cref="ContractStreamEvent{T}.Unclassified.RawKind"/>, so a server event variant
/// added after this enum was published surfaces as data rather than a dropped event or a
/// misclassification.
/// </remarks>
public enum UnclassifiedKind
{
    /// <summary>
    /// The transport delivered an event this layer has no typed variant for; the raw
    /// transport descriptor is carried on
    /// <see cref="ContractStreamEvent{T}.Unclassified.RawKind"/>. The default value, so an
    /// unset discriminator never reads as a specific known reason.
    /// </summary>
    Unknown = 0,

    /// <summary>A create event was delivered whose contract is not of the subscribed
    /// marker <c>T</c>.</summary>
    CreatedEvent,

    /// <summary>An archive event was delivered whose contract is not of the subscribed
    /// marker <c>T</c>.</summary>
    ArchivedEvent,

    /// <summary>An exercise event was delivered whose contract is not of the subscribed
    /// marker <c>T</c>.</summary>
    ExercisedEvent,

    /// <summary>An assignment (reassignment-in) event was delivered whose contract is not
    /// of the subscribed marker <c>T</c>.</summary>
    AssignedEvent,

    /// <summary>An unassignment (reassignment-out) event was delivered whose contract is
    /// not of the subscribed marker <c>T</c>.</summary>
    UnassignedEvent,

    /// <summary>A matching event was delivered without the synchronizer id required to
    /// project it into a typed variant.</summary>
    MissingSynchronizerId,

    /// <summary>A matching event was delivered on an interface subscription but carried no
    /// interface view to decode into the marker's payload.</summary>
    InterfaceViewUnavailable,

    /// <summary>A matching event's payload could not be decoded into its typed
    /// representation.</summary>
    DecodeFailure,

    /// <summary>A reassignment event was delivered carrying neither an assignment nor an
    /// unassignment.</summary>
    EmptyReassignment,
}

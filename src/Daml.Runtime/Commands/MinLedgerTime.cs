// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace Daml.Runtime.Commands;

/// <summary>
/// The earliest ledger effective time a submission may be assigned — "do not commit this
/// before T". A transport projects it onto <c>Commands.min_ledger_time_abs</c> or
/// <c>Commands.min_ledger_time_rel</c> in the Ledger API payload, one field per arm.
/// </summary>
/// <remarks>
/// <para>
/// Omitting the bound — leaving <see cref="CommandsSubmission.MinLedgerTime"/> at
/// <see langword="null"/> — imposes no constraint at all: the participant assigns the ledger
/// time itself, which is the behaviour of every submission that does not carry one. A bound is
/// a floor, not a schedule: the participant may assign a later ledger time, and a bound that
/// has already passed by the time the submission is processed delays nothing.
/// </para>
/// <para>
/// The two wire fields are mutually exclusive — a participant rejects a submission carrying
/// both — so the bound is modelled as a closed hierarchy of the two forms rather than as a
/// pair of nullable members, and a submission carrying both is unrepresentable rather than
/// rejected at dispatch. Consumers project a bound by matching the arms:
/// <see cref="Absolute"/> and <see cref="Relative"/> are the only ones.
/// </para>
/// </remarks>
public abstract record MinLedgerTime
{
    /// <summary>Sealed; new arms live alongside the existing ones.</summary>
    private protected MinLedgerTime() { }

    /// <summary>
    /// Applies the handler for this bound's arm and returns its result — the projection a
    /// transport uses to reach the one wire field this arm maps to.
    /// </summary>
    /// <remarks>
    /// Every arm is a parameter, so adding one changes this signature and a consumer that
    /// projects through it stops compiling instead of falling through a default branch. A
    /// <c>switch</c> cannot offer that: C# treats a class hierarchy as open, so a switch
    /// expression covering both arms is still non-exhaustive (CS8509) and needs a discard
    /// arm that would silently swallow a new one.
    /// </remarks>
    /// <typeparam name="TResult">The projection's result type.</typeparam>
    /// <param name="absolute">Handler for <see cref="Absolute"/>, receiving its instant.</param>
    /// <param name="relative">Handler for <see cref="Relative"/>, receiving its delay.</param>
    /// <exception cref="ArgumentNullException">Either handler is <see langword="null"/>.</exception>
    public abstract TResult Match<TResult>(
        Func<DateTimeOffset, TResult> absolute,
        Func<TimeSpan, TResult> relative);

    /// <summary>
    /// A bound expressed as an instant, projected onto <c>Commands.min_ledger_time_abs</c>.
    /// The instant is carried verbatim, including its UTC offset; converting it to the wire
    /// clock is the transport's business.
    /// </summary>
    public sealed record Absolute : MinLedgerTime
    {
        /// <summary>
        /// Creates an absolute bound.
        /// </summary>
        /// <param name="value">The instant before which the submission must not be committed.</param>
        public Absolute(DateTimeOffset value) => Value = value;

        /// <summary>The instant before which the submission must not be committed.</summary>
        public DateTimeOffset Value { get; }

        /// <inheritdoc />
        public override TResult Match<TResult>(
            Func<DateTimeOffset, TResult> absolute,
            Func<TimeSpan, TResult> relative)
        {
            ArgumentNullException.ThrowIfNull(absolute);
            ArgumentNullException.ThrowIfNull(relative);
            return absolute(Value);
        }
    }

    /// <summary>
    /// A bound expressed as a delay from the moment the participant receives the submission,
    /// projected onto <c>Commands.min_ledger_time_rel</c>. Preferred over <see cref="Absolute"/>
    /// when the submitting application's clock is not known to agree with the participant's,
    /// because a relative bound is resolved against the participant's own clock.
    /// </summary>
    public sealed record Relative : MinLedgerTime
    {
        /// <summary>
        /// Creates a relative bound.
        /// </summary>
        /// <param name="value">
        /// The delay from submission. <see cref="TimeSpan.Zero"/> is accepted and means "no
        /// earlier than now".
        /// </param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="value"/> is negative. A negative delay has no wire representation:
        /// the field is a duration into the future, and a bound in the past is expressed by
        /// omitting the bound.
        /// </exception>
        public Relative(TimeSpan value)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, TimeSpan.Zero);
            Value = value;
        }

        /// <summary>The delay from submission before which the submission must not be committed.</summary>
        public TimeSpan Value { get; }

        /// <inheritdoc />
        public override TResult Match<TResult>(
            Func<DateTimeOffset, TResult> absolute,
            Func<TimeSpan, TResult> relative)
        {
            ArgumentNullException.ThrowIfNull(absolute);
            ArgumentNullException.ThrowIfNull(relative);
            return relative(Value);
        }
    }
}

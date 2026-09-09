// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Data;
using Daml.Runtime.Streams;
using Xunit;

namespace Daml.Ledger.Abstractions.Testing.Conformance.Tests;

public sealed class HangingSubscriptionConformanceTests
{
    [Fact]
    public async Task Cancellation_test_fails_with_a_TimeoutException_when_the_transport_ignores_the_token()
    {
        var kit = new HangingSubscriptionKit();

        var run = await Record.ExceptionAsync(
            () => kit.Cancelling_a_live_subscription_throws_OperationCanceledException());

        run.Should().NotBeNull(
            "a transport that ignores the cancellation token must fail the kit's test, not hang the run");
        run!.Message.Should().Contain(nameof(TimeoutException));
        run.Message.Should().Contain("a cancelled live subscription must throw OperationCanceledException");
    }

    private sealed class HangingSubscriptionKit : LedgerClientConformanceTests<ConformanceProbe>
    {
        protected override ILedgerClient CreateClient() => new TokenIgnoringFakeClient();

        protected override SubmitterInfo Reader { get; } = new Party("alice");

        protected override TimeSpan StreamTimeout => TimeSpan.FromMilliseconds(200);
    }

    private sealed class TokenIgnoringFakeClient : NotSupportedLedgerClient
    {
        public override async IAsyncEnumerable<ContractStreamEvent<T>> SubscribeAsync<T>(
            SubmitterInfo submitter,
            LedgerOffset? fromOffset = null,
            LedgerOffset? toOffset = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            while (true)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(20), CancellationToken.None);
                yield return new ContractStreamEvent<T>.Checkpoint(LedgerOffset.Begin);
            }
        }
    }
}

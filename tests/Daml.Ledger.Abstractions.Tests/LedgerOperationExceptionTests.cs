// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Outcomes;
using AwesomeAssertions;
using Xunit;

namespace Daml.Ledger.Abstractions.Tests;

public class LedgerOperationExceptionTests
{
    [Fact]
    public void message_and_inner_exception_constructor_preserves_both()
    {
        var inner = new TimeoutException("transport gave up");

        var exception = new LedgerOperationException("operation failed", inner);

        exception.Message.Should().Be("operation failed");
        exception.InnerException.Should().BeSameAs(inner);
        exception.Category.Should().BeNull();
        exception.ErrorId.Should().BeNull();
        exception.Metadata.Should().BeNull();
        exception.StatusCode.Should().BeNull();
    }

    [Fact]
    public void daml_error_constructor_rejects_null_metadata()
    {
        var act = () => new LedgerOperationException(
            "exercise failed",
            DamlErrorCategory.InvalidGivenCurrentSystemStateOther,
            "SOME_ERROR_ID",
            metadata: null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("metadata");
    }

    [Fact]
    public void daml_error_constructor_keeps_supplied_metadata()
    {
        var metadata = new Dictionary<string, string> { ["key"] = "value" };

        var exception = new LedgerOperationException(
            "exercise failed",
            DamlErrorCategory.InvalidGivenCurrentSystemStateOther,
            "SOME_ERROR_ID",
            metadata);

        exception.Metadata.Should().BeSameAs(metadata);
    }
}

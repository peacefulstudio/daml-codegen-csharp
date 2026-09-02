// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Outcomes;
using AwesomeAssertions;
using Xunit;

namespace Daml.Ledger.Abstractions.Tests;

public class LedgerOperationExceptionTests
{
    [Fact]
    public void LedgerOperationException_message_and_inner_exception_constructor_preserves_both()
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
    public void LedgerOperationException_infra_error_constructor_leaves_category_null_when_omitted()
    {
        var exception = new LedgerOperationException("transport failed", 503);

        exception.StatusCode.Should().Be(503);
        exception.Category.Should().BeNull();
        exception.ErrorId.Should().BeNull();
        exception.Metadata.Should().BeNull();
        exception.InnerException.Should().BeNull();
    }

    [Fact]
    public void LedgerOperationException_infra_error_constructor_keeps_the_category_alongside_the_status_code()
    {
        var inner = new TimeoutException("transport gave up");

        var exception = new LedgerOperationException(
            "transport failed",
            400,
            DamlErrorCategory.InvalidIndependentOfSystemState,
            inner);

        exception.StatusCode.Should().Be(400);
        exception.Category.Should().Be(DamlErrorCategory.InvalidIndependentOfSystemState);
        exception.InnerException.Should().BeSameAs(inner);
        exception.ErrorId.Should().BeNull();
    }

    [Fact]
    public void LedgerOperationException_daml_error_constructor_rejects_null_metadata()
    {
        var act = () => new LedgerOperationException(
            "exercise failed",
            DamlErrorCategory.InvalidGivenCurrentSystemStateOther,
            "SOME_ERROR_ID",
            metadata: null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("metadata");
    }

    [Fact]
    public void LedgerOperationException_daml_error_constructor_keeps_supplied_metadata()
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

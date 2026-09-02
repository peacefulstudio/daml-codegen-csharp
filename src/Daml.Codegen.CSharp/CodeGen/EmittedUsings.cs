// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using LedgerNamespaces = Daml.Ledger.Abstractions.LedgerNamespaces;
using RuntimeNamespaces = Daml.Runtime.RuntimeNamespaces;

namespace Daml.Codegen.CSharp.CodeGen;

/// <summary>
/// The namespaces the emitted async ledger surfaces need in their file-level
/// <c>using</c> block. <see cref="RequireAsyncLedgerCallNamespaces"/> covers the
/// surfaces that call <see cref="Daml.Ledger.Abstractions.ILedgerWriter"/> members
/// directly; <see cref="RequireAsyncExerciserNamespaces"/> adds the namespace hosting
/// the <c>TrySubmitSingleAsync</c> extension method the emitted choice exercisers call
/// on top of it.
/// </summary>
internal static class EmittedUsings
{
    internal static void RequireAsyncLedgerCallNamespaces(IndentWriter indent)
    {
        indent.Require("System");
        indent.Require("System.Threading");
        indent.Require("System.Threading.Tasks");
        indent.Require(LedgerNamespaces.Abstractions);
        indent.Require(RuntimeNamespaces.Commands);
        indent.Require(RuntimeNamespaces.Contracts);
        indent.Require(RuntimeNamespaces.Outcomes);
    }

    internal static void RequireAsyncExerciserNamespaces(IndentWriter indent)
    {
        RequireAsyncLedgerCallNamespaces(indent);
        indent.Require(LedgerNamespaces.AbstractionsExtensions);
    }
}

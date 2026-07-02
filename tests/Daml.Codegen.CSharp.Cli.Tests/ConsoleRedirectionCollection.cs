// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Xunit;

namespace Daml.Codegen.CSharp.Cli.Tests;

/// <summary>
/// Serializes the test classes that drive <c>Program</c> and therefore mutate or read
/// the process-global <see cref="System.Console"/> streams (<c>Console.SetError</c>
/// captures in one class must not overlap another class's console writes). Only these
/// classes are held out of the assembly's parallel run; everything else parallelizes.
/// </summary>
[CollectionDefinition("ConsoleRedirection", DisableParallelization = true)]
public sealed class ConsoleRedirectionCollection;

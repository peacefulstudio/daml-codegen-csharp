// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Reflection;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Xunit;

namespace Daml.Testing.Conventions;

/// <summary>
/// Compiled into every test project from <c>tests/Shared</c>, so each assembly
/// lints its own test method names against
/// <c>PascalCaseSubject_snake_case_description</c>.
/// </summary>
public class TestNamingConventionTests
{
    private static readonly Regex PascalCaseSubjectThenDescription =
        new(@"^[A-Z][A-Za-z0-9]*(?:_[A-Za-z0-9]+)+$", RegexOptions.CultureInvariant);

    [Fact]
    public void TestNamingConvention_every_test_method_leads_with_a_PascalCase_subject()
    {
        var offenders = TestMethodNames()
            .Where(name => !PascalCaseSubjectThenDescription.IsMatch(name.Split('.')[^1]))
            .ToList();

        offenders.Should().BeEmpty(
            "a test name is PascalCaseSubject_snake_case_description — the subject is the member or type under " +
            "test, or the test class name minus its Tests suffix, and at least one underscore-separated clause " +
            "describes the scenario");
    }

    [Fact]
    public void TestNamingConvention_lints_a_non_empty_set_of_test_methods()
    {
        TestMethodNames().Should().NotBeEmpty(
            "a lint that discovers no test methods passes vacuously and would never catch a regression");
    }

    private static IReadOnlyList<string> TestMethodNames() =>
        Assembly.GetExecutingAssembly()
            .GetTypes()
            .SelectMany(type => type.GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
            .Where(method => method.GetCustomAttributes<FactAttribute>(inherit: true).Any())
            .Select(method => $"{method.DeclaringType!.FullName}.{method.Name}")
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
}

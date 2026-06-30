// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Data;

namespace Daml.Runtime.Stdlib;

/// <summary>
/// Daml stdlib enum DA.Date.Types.DayOfWeek. Hand-coded into Daml.Runtime since
/// daml-stdlib is a frozen stdlib package (no NuGet equivalent) whose enums are
/// referenced by Daml Finance and Splice DARs. Mirrors the wire format emitted for
/// generated enums: each constructor round-trips through <see cref="DamlEnum"/> by name.
/// </summary>
public enum DayOfWeek
{
    /// <summary>Monday.</summary>
    Monday,
    /// <summary>Tuesday.</summary>
    Tuesday,
    /// <summary>Wednesday.</summary>
    Wednesday,
    /// <summary>Thursday.</summary>
    Thursday,
    /// <summary>Friday.</summary>
    Friday,
    /// <summary>Saturday.</summary>
    Saturday,
    /// <summary>Sunday.</summary>
    Sunday,
}

/// <summary>Extension methods for <see cref="DayOfWeek"/> serialization.</summary>
public static class DayOfWeekExtensions
{
    /// <summary>Converts to a DamlEnum value.</summary>
    public static DamlEnum ToDamlEnum(this DayOfWeek value) => value switch
    {
        DayOfWeek.Monday => DamlEnum.Create("Monday"),
        DayOfWeek.Tuesday => DamlEnum.Create("Tuesday"),
        DayOfWeek.Wednesday => DamlEnum.Create("Wednesday"),
        DayOfWeek.Thursday => DamlEnum.Create("Thursday"),
        DayOfWeek.Friday => DamlEnum.Create("Friday"),
        DayOfWeek.Saturday => DamlEnum.Create("Saturday"),
        DayOfWeek.Sunday => DamlEnum.Create("Sunday"),
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
    };

    /// <summary>Creates an instance from a DamlEnum value.</summary>
    public static DayOfWeek FromDamlEnum(DamlEnum value) => value.Constructor switch
    {
        "Monday" => DayOfWeek.Monday,
        "Tuesday" => DayOfWeek.Tuesday,
        "Wednesday" => DayOfWeek.Wednesday,
        "Thursday" => DayOfWeek.Thursday,
        "Friday" => DayOfWeek.Friday,
        "Saturday" => DayOfWeek.Saturday,
        "Sunday" => DayOfWeek.Sunday,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value.Constructor, null)
    };
}

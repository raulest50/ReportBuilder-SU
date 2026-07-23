using System.Globalization;

namespace WinSbi.Olap.Core;

public sealed record ReportPeriod(
    int Year,
    IReadOnlyList<int> Months,
    string Label,
    string CommandPart);

public static class ReportPeriods
{
    public static ReportPeriod Resolve(int year, int? quarter, int? semester, string? months)
    {
        return Resolve(year, quarter, semester, annual: false, months);
    }

    public static ReportPeriod Resolve(int year, int? quarter, int? semester, bool annual, string? months)
    {
        if (year < 2000 || year > 2100)
        {
            throw new ArgumentException("--year must be a four-digit year.");
        }

        var hasQuarter = quarter.HasValue;
        var hasSemester = semester.HasValue;
        var hasAnnual = annual;
        var hasMonths = !string.IsNullOrWhiteSpace(months);
        var selectedPeriodKinds = new[] { hasQuarter, hasSemester, hasAnnual, hasMonths }.Count(static selected => selected);

        if (selectedPeriodKinds != 1)
        {
            throw new ArgumentException(
                "Use exactly one of --quarter <1-4>, --semester <1-2>, --annual, or --months <csv>.");
        }

        if (hasQuarter)
        {
            var resolvedMonths = MonthsForQuarter(quarter!.Value);
            return new ReportPeriod(year, resolvedMonths, $"{year}-Q{quarter.Value}", $"--quarter {quarter.Value}");
        }

        if (hasSemester)
        {
            var resolvedMonths = MonthsForSemester(semester!.Value);
            return new ReportPeriod(year, resolvedMonths, $"{year}-S{semester.Value}", $"--semester {semester.Value}");
        }

        if (hasAnnual)
        {
            return new ReportPeriod(year, Enumerable.Range(1, 12).ToList(), $"{year}-A", "--annual");
        }

        var manualMonths = ParseMonths(months!);
        var label = $"{year}-{string.Join("-", manualMonths.Select(static month => $"M{month:00}"))}";
        return new ReportPeriod(year, manualMonths, label, $"--months {string.Join(",", manualMonths)}");
    }

    public static IReadOnlyList<int> MonthsForQuarter(int quarter)
    {
        return quarter switch
        {
            1 => [1, 2, 3],
            2 => [4, 5, 6],
            3 => [7, 8, 9],
            4 => [10, 11, 12],
            _ => throw new ArgumentException("--quarter must be 1, 2, 3, or 4.")
        };
    }

    public static IReadOnlyList<int> MonthsForSemester(int semester)
    {
        return semester switch
        {
            1 => [1, 2, 3, 4, 5, 6],
            2 => [7, 8, 9, 10, 11, 12],
            _ => throw new ArgumentException("--semester must be 1 or 2.")
        };
    }

    public static IReadOnlyList<int> ParseMonths(string months)
    {
        var parsed = months
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(item => int.TryParse(item, NumberStyles.None, CultureInfo.InvariantCulture, out var month)
                ? month
                : throw new ArgumentException("--months must be a comma-separated list of month numbers."))
            .Distinct()
            .Order()
            .ToList();

        if (parsed.Count == 0 || parsed.Any(static month => month < 1 || month > 12))
        {
            throw new ArgumentException("--months values must be between 1 and 12.");
        }

        return parsed;
    }
}

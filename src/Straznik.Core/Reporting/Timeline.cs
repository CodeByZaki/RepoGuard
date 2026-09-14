using Straznik.Core.Model;

namespace Straznik.Core.Reporting;

public sealed record TimelineBucket(int Year, int Month, string Label, int Count, Severity Worst);

/// <summary>
/// Rozklada terminy na miesiace. Lista znalezisk mowi "co", os czasu mowi "kiedy sie zagesci" -
/// i to drugie jest tym, co czlowiek chce zobaczyc jednym spojrzeniem.
/// </summary>
public static class Timeline
{
    private static readonly string[] MonthLabels =
        ["sty", "lut", "mar", "kwi", "maj", "cze", "lip", "sie", "wrz", "paź", "lis", "gru"];

    public static IReadOnlyList<TimelineBucket> Build(ScanResult result, int months = 12)
    {
        var buckets = new List<TimelineBucket>(months);
        var start = new DateOnly(result.Today.Year, result.Today.Month, 1);

        for (int i = 0; i < months; i++)
        {
            var month = start.AddMonths(i);

            var inMonth = result.Findings
                .Where(f => f.Deadline is { } d
                            && d.Year == month.Year
                            && d.Month == month.Month
                            && d >= result.Today)
                .ToList();

            buckets.Add(new TimelineBucket(
                month.Year,
                month.Month,
                MonthLabels[month.Month - 1],
                inMonth.Count,
                inMonth.Count == 0 ? Severity.Info : inMonth.Max(f => f.Severity)));
        }

        return buckets;
    }

    public static string MonthLabel(int month) => MonthLabels[month - 1];
}

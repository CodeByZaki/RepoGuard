using System.Text.RegularExpressions;

namespace Straznik.Core.Dates;

/// <summary>Data znaleziona w tekscie razem z fragmentem, ktory ja wskazal.</summary>
public readonly record struct FoundDate(DateOnly Date, string Matched, DatePrecision Precision);

public enum DatePrecision
{
    Day,
    Month,
    Quarter,
}

/// <summary>
/// Wyciaga daty z ludzkiego tekstu - komentarzy, komunikatow [Obsolete], pol JSON.
/// Programisci pisza terminy na kilkanascie sposobow, wiec Straznik rozumie kilkanascie sposobow.
/// </summary>
public static class DateHunter
{
    private const int MinYear = 2000;
    private const int MaxYear = 2100;

    private static readonly RegexOptions Opts =
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled;

    // 2026-03-14 albo 2026/03/14
    private static readonly Regex Iso = new(@"\b(?<y>\d{4})[-/](?<m>\d{1,2})[-/](?<d>\d{1,2})\b", Opts);

    // 14.03.2026 albo 14/03/2026 - format europejski, dzien pierwszy
    private static readonly Regex European = new(@"\b(?<d>\d{1,2})[./](?<m>\d{1,2})[./](?<y>\d{4})\b", Opts);

    // 2026-03 - sam miesiac, termin liczymy na jego koniec
    private static readonly Regex YearMonth = new(@"\b(?<y>\d{4})-(?<m>\d{1,2})\b(?![-/]\d)", Opts);

    // Q2 2026 / 2026 Q2 / kw. 2 2026
    private static readonly Regex Quarter = new(
        @"\b(?:q(?<q>[1-4])\s*(?<y>\d{4})|(?<y2>\d{4})\s*q(?<q2>[1-4]))\b", Opts);

    // "marzec 2026", "March 2026", "do marca 2026"
    private static readonly Regex MonthName = new(
        @"\b(?<name>[\p{L}]{3,12})\s+(?<y>\d{4})\b", Opts);

    /// <summary>
    /// Nazwy miesiecy po polsku i angielsku. Polskie w trzech formach, bo termin pisze sie
    /// na trzy sposoby: "marzec 2026" (mianownik), "do marca 2026" (dopelniacz),
    /// "w marcu 2026" (miejscownik).
    /// </summary>
    private static readonly Dictionary<string, int> MonthNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["styczeń"] = 1, ["stycznia"] = 1, ["styczniu"] = 1, ["sty"] = 1, ["january"] = 1, ["jan"] = 1,
        ["luty"] = 2, ["lutego"] = 2, ["lutym"] = 2, ["lut"] = 2, ["february"] = 2, ["feb"] = 2,
        ["marzec"] = 3, ["marca"] = 3, ["marcu"] = 3, ["mar"] = 3, ["march"] = 3,
        ["kwiecień"] = 4, ["kwietnia"] = 4, ["kwietniu"] = 4, ["kwi"] = 4, ["april"] = 4, ["apr"] = 4,
        ["maj"] = 5, ["maja"] = 5, ["maju"] = 5, ["may"] = 5,
        ["czerwiec"] = 6, ["czerwca"] = 6, ["czerwcu"] = 6, ["cze"] = 6, ["june"] = 6, ["jun"] = 6,
        ["lipiec"] = 7, ["lipca"] = 7, ["lipcu"] = 7, ["lip"] = 7, ["july"] = 7, ["jul"] = 7,
        ["sierpień"] = 8, ["sierpnia"] = 8, ["sierpniu"] = 8, ["sie"] = 8, ["august"] = 8, ["aug"] = 8,
        ["wrzesień"] = 9, ["września"] = 9, ["wrześniu"] = 9, ["wrz"] = 9,
        ["september"] = 9, ["sep"] = 9, ["sept"] = 9,
        ["październik"] = 10, ["października"] = 10, ["październiku"] = 10, ["paź"] = 10,
        ["october"] = 10, ["oct"] = 10,
        ["listopad"] = 11, ["listopada"] = 11, ["listopadzie"] = 11, ["lis"] = 11,
        ["november"] = 11, ["nov"] = 11,
        ["grudzień"] = 12, ["grudnia"] = 12, ["grudniu"] = 12, ["gru"] = 12, ["december"] = 12, ["dec"] = 12,
    };

    /// <summary>
    /// Znajduje pierwsza sensowna date w tekscie. Kolejnosc prob jest celowa:
    /// od formatow najbardziej jednoznacznych do najbardziej swobodnych.
    /// </summary>
    public static FoundDate? Find(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (TryIso(text, out var iso))
        {
            return iso;
        }

        if (TryEuropean(text, out var eu))
        {
            return eu;
        }

        if (TryYearMonth(text, out var ym))
        {
            return ym;
        }

        if (TryQuarter(text, out var q))
        {
            return q;
        }

        return TryMonthName(text, out var mn) ? mn : null;
    }

    private static bool TryIso(string text, out FoundDate found)
    {
        foreach (Match m in Iso.Matches(text))
        {
            if (TryBuild(Num(m, "y"), Num(m, "m"), Num(m, "d"), m.Value, DatePrecision.Day, out found))
            {
                return true;
            }
        }

        found = default;
        return false;
    }

    private static bool TryEuropean(string text, out FoundDate found)
    {
        foreach (Match m in European.Matches(text))
        {
            if (TryBuild(Num(m, "y"), Num(m, "m"), Num(m, "d"), m.Value, DatePrecision.Day, out found))
            {
                return true;
            }
        }

        found = default;
        return false;
    }

    private static bool TryYearMonth(string text, out FoundDate found)
    {
        foreach (Match m in YearMonth.Matches(text))
        {
            int year = Num(m, "y");
            int month = Num(m, "m");

            // Termin podany z dokladnoscia do miesiaca liczymy na ostatni dzien tego miesiaca -
            // znacznik z data "2026-03" znaczy "do konca marca", nie "pierwszego marca".
            if (IsSaneMonth(year, month)
                && TryBuild(year, month, DateTime.DaysInMonth(year, month), m.Value, DatePrecision.Month, out found))
            {
                return true;
            }
        }

        found = default;
        return false;
    }

    private static bool TryQuarter(string text, out FoundDate found)
    {
        foreach (Match m in Quarter.Matches(text))
        {
            int quarter = m.Groups["q"].Success ? Num(m, "q") : Num(m, "q2");
            int year = m.Groups["y"].Success ? Num(m, "y") : Num(m, "y2");
            int month = quarter * 3;

            if (IsSaneMonth(year, month)
                && TryBuild(year, month, DateTime.DaysInMonth(year, month), m.Value, DatePrecision.Quarter, out found))
            {
                return true;
            }
        }

        found = default;
        return false;
    }

    private static bool TryMonthName(string text, out FoundDate found)
    {
        foreach (Match m in MonthName.Matches(text))
        {
            if (!MonthNames.TryGetValue(m.Groups["name"].Value, out int month))
            {
                continue;
            }

            int year = Num(m, "y");
            if (IsSaneMonth(year, month)
                && TryBuild(year, month, DateTime.DaysInMonth(year, month), m.Value, DatePrecision.Month, out found))
            {
                return true;
            }
        }

        found = default;
        return false;
    }

    private static bool TryBuild(int year, int month, int day, string matched, DatePrecision precision, out FoundDate found)
    {
        found = default;

        if (!IsSaneMonth(year, month) || day is < 1 || day > DateTime.DaysInMonth(year, month))
        {
            return false;
        }

        found = new FoundDate(new DateOnly(year, month, day), matched.Trim(), precision);
        return true;
    }

    private static bool IsSaneMonth(int year, int month) =>
        year is >= MinYear and <= MaxYear && month is >= 1 and <= 12;

    private static int Num(Match m, string group) => int.Parse(m.Groups[group].Value);
}

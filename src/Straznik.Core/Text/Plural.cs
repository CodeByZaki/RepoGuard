namespace Straznik.Core.Text;

/// <summary>
/// Polska odmiana liczebnikow. Raport, ktory pisze "4 plików", wyglada jak wygenerowany
/// automatem - a Straznik ma brzmiec jak notatka od kolegi z zespolu.
/// </summary>
public static class Plural
{
    /// <summary>
    /// Wybiera forme: pojedyncza (1 plik), mnoga (2 pliki), dopelniaczowa (5 plików).
    /// </summary>
    public static string Form(int count, string one, string few, string many)
    {
        if (Math.Abs(count) == 1)
        {
            return one;
        }

        int lastTwo = Math.Abs(count) % 100;
        int last = Math.Abs(count) % 10;

        // 12, 13, 14 ida do formy dopelniaczowej mimo koncowki 2/3/4.
        return last is >= 2 and <= 4 && lastTwo is < 12 or > 14 ? few : many;
    }

    public static string With(int count, string one, string few, string many) =>
        $"{count} {Form(count, one, few, many)}";

    public static string Files(int count) => With(count, "plik", "pliki", "plików");

    public static string Projects(int count) => With(count, "projekt", "projekty", "projektów");

    public static string Inspectors(int count) => With(count, "inspektor", "inspektorzy", "inspektorów");

    public static string Usages(int count) => With(count, "użycie", "użycia", "użyć");

    public static string Markers(int count) => With(count, "znacznik", "znaczniki", "znaczników");

    public static string Days(int count) => With(count, "dzień", "dni", "dni");

    public static string Months(int count) => With(count, "miesiąc", "miesiące", "miesięcy");
}

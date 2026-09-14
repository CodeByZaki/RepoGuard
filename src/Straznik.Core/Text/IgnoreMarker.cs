namespace Straznik.Core.Text;

/// <summary>
/// Furtka dla swiadomych wyjatkow: linia z "straznik:ignore" jest pomijana.
///
/// Kazde narzedzie tego typu jej potrzebuje, bo zawsze istnieje data, ktora ma prawo
/// tam byc - przyklad w dokumentacji, dana testowa, stala z ustawy. Bez takiej furtki
/// zespol wycisza calego Straznika, a to gorsze niz jeden pominiety wiersz.
/// </summary>
public static class IgnoreMarker
{
    public const string Token = "straznik:ignore";

    public static bool IsIgnored(string? text) =>
        text is not null && text.Contains(Token, StringComparison.OrdinalIgnoreCase);
}

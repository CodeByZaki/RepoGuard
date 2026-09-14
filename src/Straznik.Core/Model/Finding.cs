namespace Straznik.Core.Model;

/// <summary>
/// Jedna rzecz w repozytorium, ktora ma date waznosci.
/// </summary>
public sealed record Finding
{
    /// <summary>Identyfikator inspektora, ktory to znalazl (np. "datedComments").</summary>
    public required string Inspector { get; init; }

    /// <summary>Krotka etykieta kategorii pokazywana w raporcie (np. "TODO", "Feature flaga").</summary>
    public required string Category { get; init; }

    /// <summary>Jednozdaniowy opis znaleziska.</summary>
    public required string Title { get; init; }

    /// <summary>Gdzie to jest - sciezka wzgledna, opcjonalnie z numerem linii lub sciezka JSON.</summary>
    public required string Location { get; init; }

    /// <summary>Dodatkowy kontekst: tresc komentarza, liczba uzyc, powod wycofania.</summary>
    public string? Detail { get; init; }

    /// <summary>Kiedy to wygasa. Null oznacza znalezisko bez konkretnej daty.</summary>
    public DateOnly? Deadline { get; init; }

    /// <summary>
    /// Ile dni zostalo do terminu. Wartosc ujemna = tyle dni po terminie.
    /// Null, gdy znalezisko nie ma daty.
    /// </summary>
    public int? DaysLeft { get; init; }

    public required Severity Severity { get; init; }

    /// <summary>Podpowiedz, co z tym zrobic.</summary>
    public string? Hint { get; init; }

    /// <summary>Stabilny klucz znaleziska - do porownywania raportow miedzy uruchomieniami.</summary>
    public string Key => $"{Inspector}|{Location}|{Title}";

    public bool IsOverdue => DaysLeft is < 0;
}

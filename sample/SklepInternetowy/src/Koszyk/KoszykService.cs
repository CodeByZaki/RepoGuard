namespace SklepInternetowy.Koszyk;

/// <summary>
/// Przykładowy serwis koszyka. Wygląda jak każdy inny plik w każdym innym projekcie -
/// i dokładnie dlatego nikt nie zauważa, ile terminów w nim zapadło.
/// </summary>
public sealed class KoszykService(IKoszykRepozytorium repozytorium)
{
    // Stara ścieżka zapisu koszyka. Zostawiamy na czas migracji danych.
    // TODO(2025-03-01): usunąć fallback po zakończeniu migracji koszyków
    private const bool UzywajStaregoZapisu = true;

    // HACK: obejście limitu API magazynu, do końca marca 2026 zanim wejdzie nowy kontrakt
    private const int LimitPozycji = 50;

    // TODO: przenieść walidację do osobnej klasy
    // FIXME: brzydko się to czyta, ale działa
    private readonly IKoszykRepozytorium _repozytorium = repozytorium;

    /// <summary>Data, od której koszyk przechodzi na nowy model rabatów.</summary>
    private static readonly DateTime PrzelaczenieNaNoweRabaty = new(2026, 11, 1);

    public async Task<Koszyk> WczytajAsync(Guid klientId, CancellationToken ct)
    {
        var koszyk = await _repozytorium.PobierzAsync(klientId, ct);

        if (DateTime.UtcNow >= PrzelaczenieNaNoweRabaty)
        {
            koszyk.PrzeliczRabatyWNowymModelu();
        }

        return koszyk;
    }

    // XXX: metoda robi za dużo, ale nikt nie ma czasu tego rozbić
    public async Task DodajPozycjeAsync(Guid klientId, PozycjaKoszyka pozycja, CancellationToken ct)
    {
        var koszyk = await WczytajAsync(klientId, ct);

        if (koszyk.Pozycje.Count >= LimitPozycji)
        {
            throw new InvalidOperationException($"Koszyk może mieć najwyżej {LimitPozycji} pozycji.");
        }

        koszyk.Dodaj(pozycja);

        if (UzywajStaregoZapisu)
        {
            await _repozytorium.ZapiszStarymSchematemAsync(koszyk, ct);
        }

        await _repozytorium.ZapiszAsync(koszyk, ct);
    }
}

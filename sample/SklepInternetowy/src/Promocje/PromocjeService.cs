namespace SklepInternetowy.Promocje;

public sealed class PromocjeService
{
    /// <summary>Świąteczna promocja z zeszłego sezonu. Kod wciąż jej pilnuje.</summary>
    private static readonly DateTime PromocjaSwiatecznaKoniec = new(2025, 12, 24);

    /// <summary>Data, po której stary cennik przestaje obowiązywać.</summary>
    private static readonly DateOnly StaryCennikWaznyDo = new(2026, 10, 31);

    // TODO(2026-12-31): rozbić PromocjeService na strategie, bo doszła piąta reguła
    public decimal Przelicz(decimal cena, DateTime teraz)
    {
        if (teraz <= PromocjaSwiatecznaKoniec)
        {
            return cena * 0.8m;
        }

        return DateOnly.FromDateTime(teraz) <= StaryCennikWaznyDo
            ? cena * 0.95m
            : cena;
    }
}

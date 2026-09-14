namespace SklepInternetowy.Platnosci;

/// <summary>Klient bramki płatniczej — wersja pierwsza, formalnie już wycofana.</summary>
[Obsolete("Używaj PlatnosciClientV2. Planowane usunięcie: 2026-09-30.")]
public sealed class PlatnosciClient
{
    private readonly HttpClient _http;

    public PlatnosciClient(HttpClient http) => _http = http;

    public Task<bool> ObciazAsync(decimal kwota, CancellationToken ct) =>
        Task.FromResult(kwota > 0);
}

/// <summary>Nowa bramka. Docelowa, ale migracja nadal trwa.</summary>
public sealed class PlatnosciClientV2(HttpClient http)
{
    // TODO(2026-10-05): przepiąć ostatnie dwa moduły z PlatnosciClient i skasować starą klasę
    private readonly HttpClient _http = http;

    public Task<bool> ObciazAsync(decimal kwota, CancellationToken ct) =>
        Task.FromResult(kwota > 0);
}

public sealed class PlatnosciFasada
{
    // Dwa miejsca, które wciąż wołają starego klienta.
    private readonly PlatnosciClient _stary;
    private readonly PlatnosciClientV2 _nowy;

    public PlatnosciFasada(PlatnosciClient stary, PlatnosciClientV2 nowy)
    {
        _stary = stary;
        _nowy = nowy;
    }

    public Task<bool> ZaplacAsync(decimal kwota, bool nowaSciezka, CancellationToken ct) =>
        nowaSciezka ? _nowy.ObciazAsync(kwota, ct) : _stary.ObciazAsync(kwota, ct);
}

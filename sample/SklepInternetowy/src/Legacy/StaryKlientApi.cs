namespace SklepInternetowy.Legacy;

/// <summary>
/// Klasa, o której wszyscy zapomnieli. Nikt jej nie woła od ponad dwóch lat,
/// ale nikt też nie ma pewności, że można ją usunąć - więc leży.
/// </summary>
[Obsolete("Wycofane od 2024-03. Zastąpione przez KlientApi.")]
public sealed class StaryKlientApi
{
    public Task<string> PobierzDaneAsync(Guid klientId) => Task.FromResult(klientId.ToString());
}

public sealed class KlientApi
{
    [Obsolete("Nieużywane od czasu przejścia na paginację kursorową.")]
    public Task<IReadOnlyList<string>> PobierzWszystkichAsync() =>
        Task.FromResult<IReadOnlyList<string>>([]);

    public Task<IReadOnlyList<string>> PobierzStronaAsync(string? kursor, int rozmiar) =>
        Task.FromResult<IReadOnlyList<string>>([]);
}

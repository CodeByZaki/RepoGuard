using Straznik.Core.Configuration;
using Straznik.Core.Model;
using Straznik.Core.Scanning;

namespace Straznik.Core.Inspectors;

/// <summary>
/// Jeden rodzaj "daty waznosci" w repozytorium.
/// Nowy inspektor = nowa klasa, zero zmian w reszcie Straznika.
/// </summary>
public interface IInspector
{
    /// <summary>Techniczny identyfikator, uzywany w konfiguracji i raportach.</summary>
    string Id { get; }

    /// <summary>Nazwa pokazywana czlowiekowi.</summary>
    string DisplayName { get; }

    bool IsEnabled(StraznikConfig config);

    Task<IReadOnlyList<Finding>> InspectAsync(ScanContext ctx, CancellationToken ct);
}

/// <summary>Baza dla inspektorow, ktore pracuja synchronicznie na plikach.</summary>
public abstract class SyncInspector : IInspector
{
    public abstract string Id { get; }
    public abstract string DisplayName { get; }
    public abstract bool IsEnabled(StraznikConfig config);

    protected abstract IEnumerable<Finding> Inspect(ScanContext ctx, CancellationToken ct);

    public Task<IReadOnlyList<Finding>> InspectAsync(ScanContext ctx, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Finding>>(Inspect(ctx, ct).ToList());
}

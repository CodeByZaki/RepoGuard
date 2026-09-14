namespace Straznik.Core.Model;

/// <summary>Statystyki przebiegu - to, co Straznik obejrzal, zanim cokolwiek zglosil.</summary>
public sealed record ScanStats
{
    public int CSharpFiles { get; init; }
    public int ProjectFiles { get; init; }
    public int ConfigFiles { get; init; }

    /// <summary>Znaczniki TODO/FIXME/HACK bez daty - swiadomie pominiete, ale warto znac skale.</summary>
    public int UndatedMarkers { get; init; }

    public TimeSpan Duration { get; init; }
}

/// <summary>Informacja o inspektorze, ktory nie mogl dokonczyc pracy (np. brak sieci).</summary>
public sealed record InspectorProblem(string Inspector, string Message);

public sealed record ScanResult
{
    public required string RootPath { get; init; }
    public required DateOnly Today { get; init; }
    public required IReadOnlyList<Finding> Findings { get; init; }
    public required IReadOnlyList<string> RanInspectors { get; init; }
    public IReadOnlyList<InspectorProblem> Problems { get; init; } = [];
    public ScanStats Stats { get; init; } = new();

    public int Count(Severity severity) => Findings.Count(f => f.Severity == severity);

    public Severity? Worst => Findings.Count == 0 ? null : Findings.Max(f => f.Severity);

    /// <summary>Znaleziska posortowane tak, jak chce je zobaczyc czlowiek: najpilniejsze na gorze.</summary>
    public IEnumerable<Finding> Ordered => Findings
        .OrderByDescending(f => f.Severity)
        .ThenBy(f => f.DaysLeft ?? int.MaxValue)
        .ThenBy(f => f.Location, StringComparer.OrdinalIgnoreCase);
}

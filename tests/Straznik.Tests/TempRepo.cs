using Straznik.Core.Configuration;
using Straznik.Core.Model;
using Straznik.Core.Scanning;

namespace Straznik.Tests;

/// <summary>
/// Jednorazowe repozytorium na dysku. Inspektory czytaja prawdziwe pliki,
/// wiec testy tez daja im prawdziwe pliki - zamiast atrapy systemu plikow.
/// </summary>
public sealed class TempRepo : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(),
        "straznik-testy",
        Guid.NewGuid().ToString("N"));

    public TempRepo() => Directory.CreateDirectory(Path);

    public TempRepo Write(string relativePath, string content)
    {
        string full = System.IO.Path.Combine(Path, relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        return this;
    }

    /// <summary>Uruchamia Straznika na tym repozytorium z ustalona data kontroli.</summary>
    public ScanResult Scan(string today = "2026-09-13", Action<StraznikConfig>? configure = null)
    {
        var config = new StraznikConfig();
        configure?.Invoke(config);

        return new Guard()
            .RunAsync(Path, config, DateOnly.ParseExact(today, "yyyy-MM-dd"))
            .GetAwaiter()
            .GetResult();
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // Sprzatanie katalogu tymczasowego nie moze wywrocic testu.
        }
    }
}

public static class ScanResultAssertions
{
    public static Finding Single(this ScanResult result, string inspector) =>
        Assert.Single(result.Findings, f => f.Inspector == inspector);

    public static IReadOnlyList<Finding> From(this ScanResult result, string inspector) =>
        [.. result.Findings.Where(f => f.Inspector == inspector)];
}

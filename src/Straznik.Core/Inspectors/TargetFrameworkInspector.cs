using System.Xml.Linq;
using Straznik.Core.Configuration;
using Straznik.Core.Model;
using Straznik.Core.Scanning;

namespace Straznik.Core.Inspectors;

/// <summary>
/// Sprawdza, czy TargetFramework projektow nie zblizyl sie do konca wsparcia Microsoftu.
/// Data konca wsparcia jest znana z gory na lata - i mimo to zawsze zaskakuje.
/// </summary>
public sealed class TargetFrameworkInspector : SyncInspector
{
    public override string Id => "targetFramework";
    public override string DisplayName => "Wsparcie .NET";

    public override bool IsEnabled(StraznikConfig config) => config.TargetFramework.Enabled;

    /// <summary>
    /// Oficjalne daty konca wsparcia (dotnet.microsoft.com/platform/support/policy/dotnet-core).
    /// Tabela jest wbudowana celowo: Straznik ma dzialac takze bez dostepu do sieci.
    /// </summary>
    private static readonly Dictionary<string, (DateOnly EndOfSupport, string Track)> Lifecycle = new(StringComparer.OrdinalIgnoreCase)
    {
        ["netcoreapp2.1"] = (new DateOnly(2021, 8, 21), "LTS"),
        ["netcoreapp3.1"] = (new DateOnly(2022, 12, 13), "LTS"),
        ["net5.0"] = (new DateOnly(2022, 5, 10), "STS"),
        ["net6.0"] = (new DateOnly(2024, 11, 12), "LTS"),
        ["net7.0"] = (new DateOnly(2024, 5, 14), "STS"),
        ["net8.0"] = (new DateOnly(2026, 11, 10), "LTS"),
        ["net9.0"] = (new DateOnly(2026, 5, 12), "STS"),
        ["net10.0"] = (new DateOnly(2028, 11, 14), "LTS"),
    };

    protected override IEnumerable<Finding> Inspect(ScanContext ctx, CancellationToken ct)
    {
        // Coraz wiecej repozytoriow trzyma TargetFramework centralnie w Directory.Build.props,
        // a nie w kazdym .csproj z osobna - Straznik musi zajrzec w oba miejsca.
        var sources = ctx.ProjectFiles
            .Concat(ctx.Scanner.Find("**/Directory.Build.props"))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (string project in sources)
        {
            ct.ThrowIfCancellationRequested();

            foreach (var finding in InspectProject(ctx, project))
            {
                yield return finding;
            }
        }

        foreach (var finding in InspectGlobalJson(ctx))
        {
            yield return finding;
        }
    }

    private IEnumerable<Finding> InspectProject(ScanContext ctx, string project)
    {
        XDocument document;
        try
        {
            document = XDocument.Load(project);
        }
        catch (Exception e) when (e is System.Xml.XmlException or IOException or UnauthorizedAccessException)
        {
            yield break;
        }

        foreach (string moniker in Monikers(document))
        {
            string normalized = StripPlatform(moniker);

            if (!Lifecycle.TryGetValue(normalized, out var info))
            {
                continue;
            }

            yield return ctx.Grader.WithDeadline(
                new Finding
                {
                    Inspector = Id,
                    Category = "TargetFramework",
                    Title = $"{Path.GetFileName(project)} celuje w {moniker}",
                    Location = ctx.Relative(project),
                    Detail = $"Koniec wsparcia {info.Track}: {info.EndOfSupport:yyyy-MM-dd}.",
                    Severity = Severity.Info,
                    Hint = "Po tej dacie nie ma poprawek bezpieczeństwa. Zaplanuj podniesienie wersji.",
                },
                info.EndOfSupport);
        }
    }

    private IEnumerable<Finding> InspectGlobalJson(ScanContext ctx)
    {
        foreach (string file in ctx.Scanner.Find("**/global.json"))
        {
            string? version = ReadSdkVersion(file);
            if (version is null)
            {
                continue;
            }

            string moniker = MonikerFromSdk(version);
            if (!Lifecycle.TryGetValue(moniker, out var info))
            {
                continue;
            }

            yield return ctx.Grader.WithDeadline(
                new Finding
                {
                    Inspector = Id,
                    Category = "SDK",
                    Title = $"global.json przypina SDK {version}",
                    Location = ctx.Relative(file),
                    Detail = $"Pasmo {moniker}, koniec wsparcia {info.Track}: {info.EndOfSupport:yyyy-MM-dd}.",
                    Severity = Severity.Info,
                    Hint = "Przypięty SDK po końcu wsparcia zatrzymuje cały zespół na starej wersji.",
                },
                info.EndOfSupport);
        }
    }

    private static string? ReadSdkVersion(string file)
    {
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(file));

            return document.RootElement.TryGetProperty("sdk", out var sdk)
                   && sdk.TryGetProperty("version", out var version)
                   && version.ValueKind == System.Text.Json.JsonValueKind.String
                ? version.GetString()
                : null;
        }
        catch (Exception e) when (e is System.Text.Json.JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>"8.0.404" → "net8.0"</summary>
    private static string MonikerFromSdk(string version)
    {
        string[] parts = version.Split('.');
        return parts.Length >= 2 ? $"net{parts[0]}.{parts[1]}" : version;
    }

    /// <summary>"net8.0-windows10.0.19041.0" → "net8.0"</summary>
    private static string StripPlatform(string moniker)
    {
        int dash = moniker.IndexOf('-');
        return dash < 0 ? moniker : moniker[..dash];
    }

    private static IEnumerable<string> Monikers(XDocument document)
    {
        var names = new[] { "TargetFramework", "TargetFrameworks" };

        return document.Descendants()
            .Where(e => names.Contains(e.Name.LocalName, StringComparer.Ordinal))
            .SelectMany(e => e.Value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(v => !v.Contains('$'))
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }
}

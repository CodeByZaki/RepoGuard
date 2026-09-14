using System.Collections.Concurrent;
using System.Text.Json;
using System.Xml.Linq;
using Straznik.Core.Configuration;
using Straznik.Core.Model;
using Straznik.Core.Scanning;
using Straznik.Core.Text;

namespace Straznik.Core.Inspectors;

/// <summary>
/// Data waznosci zaleznosci: pakiety wycofane przez autora, z podatnoscia,
/// wycofane z listy albo takie, ktore od lat nie doczekaly sie wydania.
/// </summary>
public sealed class NuGetInspector(HttpClient? httpClient = null) : IInspector
{
    public string Id => "nuget";
    public string DisplayName => "Zależności NuGet";

    public bool IsEnabled(StraznikConfig config) => config.NuGet.Enabled;

    private const string SearchApi = "https://azuresearch-usnc.nuget.org/query";
    private const string RegistrationApi = "https://api.nuget.org/v3/registration5-semver1";

    /// <summary>Ile zapytan do nuget.org rownolegle. Straznik ma byc uprzejmym gosciem.</summary>
    private const int Parallelism = 4;

    private readonly HttpClient _http = httpClient ?? new HttpClient();

    public async Task<IReadOnlyList<Finding>> InspectAsync(ScanContext ctx, CancellationToken ct)
    {
        var packages = CollectPackages(ctx);
        if (packages.Count == 0)
        {
            return [];
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(ctx.Config.NuGet.TimeoutSeconds * 4));

        var findings = new ConcurrentBag<Finding>();
        using var gate = new SemaphoreSlim(Parallelism);

        var work = packages.Select(async package =>
        {
            await gate.WaitAsync(timeout.Token);
            try
            {
                foreach (var finding in await InspectPackageAsync(ctx, package, timeout.Token))
                {
                    findings.Add(finding);
                }
            }
            finally
            {
                gate.Release();
            }
        });

        await Task.WhenAll(work);

        return [.. findings];
    }

    private sealed record PackageReference(string Id, string? Version, string Project);

    private async Task<IReadOnlyList<Finding>> InspectPackageAsync(
        ScanContext ctx,
        PackageReference package,
        CancellationToken ct)
    {
        var findings = new List<Finding>();
        var metadata = await FetchSearchAsync(package.Id, ct);

        if (metadata is null)
        {
            return findings;
        }

        if (metadata.Deprecation is { } deprecation)
        {
            findings.Add(new Finding
            {
                Inspector = Id,
                Category = "Pakiet wycofany",
                Title = $"{package.Id} został wycofany przez autora",
                Location = package.Project,
                Detail = Join(
                    deprecation.Reasons.Count > 0 ? $"Powód: {string.Join(", ", deprecation.Reasons)}" : null,
                    deprecation.Alternate is null ? null : $"Zalecany zamiennik: {deprecation.Alternate}",
                    Shorten(deprecation.Message)),
                Severity = Severity.Expired,
                Hint = deprecation.Alternate is null
                    ? "Autor przestał go utrzymywać. Zaplanuj wyjście."
                    : $"Przejdź na {deprecation.Alternate}.",
            });
        }

        if (metadata.Vulnerabilities > 0)
        {
            findings.Add(new Finding
            {
                Inspector = Id,
                Category = "Podatność",
                Title = $"{package.Id} ma zgłoszone podatności ({metadata.Vulnerabilities})",
                Location = package.Project,
                Detail = $"Używana wersja: {package.Version ?? "nieokreślona"}.",
                Severity = Severity.Expired,
                Hint = "Sprawdź szczegóły: dotnet list package --vulnerable.",
            });
        }

        var release = await FetchLatestReleaseAsync(package.Id, ct);
        if (release is null)
        {
            return findings;
        }

        if (!release.Value.Listed)
        {
            findings.Add(new Finding
            {
                Inspector = Id,
                Category = "Pakiet ukryty",
                Title = $"{package.Id} nie jest już wystawiony w galerii",
                Location = package.Project,
                Detail = $"Ostatnie wydanie {release.Value.Version} z {release.Value.Published:yyyy-MM-dd}.",
                Severity = Severity.Warning,
                Hint = "Nowe osoby w zespole nie znajdą tego pakietu. Rozważ zamiennik.",
            });
        }

        int staleAfter = ctx.Config.NuGet.StaleAfterMonths;
        var staleDeadline = release.Value.Published.AddMonths(staleAfter);

        if (staleDeadline <= ctx.Today)
        {
            int months = ((ctx.Today.Year - release.Value.Published.Year) * 12)
                         + ctx.Today.Month - release.Value.Published.Month;

            findings.Add(ctx.Grader.WithDeadline(
                new Finding
                {
                    Inspector = Id,
                    Category = "Pakiet bez wydań",
                    Title = $"{package.Id} — ostatnie wydanie {Plural.Months(months)} temu",
                    Location = package.Project,
                    Detail = $"Wersja {release.Value.Version} z {release.Value.Published:yyyy-MM-dd}.",
                    Severity = Severity.Info,
                    Hint = $"Polityka repo: pakiet bez wydania przez {Plural.Months(staleAfter)} traktujemy jak porzucony.",
                },
                staleDeadline));
        }

        return findings;
    }

    private readonly record struct DeprecationInfo(IReadOnlyList<string> Reasons, string? Alternate, string? Message);

    private sealed record SearchMetadata(DeprecationInfo? Deprecation, int Vulnerabilities);

    private async Task<SearchMetadata?> FetchSearchAsync(string id, CancellationToken ct)
    {
        string url = $"{SearchApi}?q=packageid:{Uri.EscapeDataString(id)}&prerelease=true&semVerLevel=2.0.0";
        var root = await GetJsonAsync(url, ct);

        if (root is null
            || !root.Value.TryGetProperty("data", out var data)
            || data.GetArrayLength() == 0)
        {
            return null;
        }

        var entry = data[0];
        DeprecationInfo? deprecation = null;

        if (entry.TryGetProperty("deprecation", out var dep) && dep.ValueKind == JsonValueKind.Object)
        {
            var reasons = dep.TryGetProperty("reasons", out var r) && r.ValueKind == JsonValueKind.Array
                ? r.EnumerateArray().Select(x => x.GetString() ?? string.Empty).Where(x => x.Length > 0).ToList()
                : [];

            string? alternate = dep.TryGetProperty("alternatePackage", out var alt)
                                && alt.ValueKind == JsonValueKind.Object
                                && alt.TryGetProperty("id", out var altId)
                ? altId.GetString()
                : null;

            string? message = dep.TryGetProperty("message", out var msg) ? msg.GetString() : null;

            deprecation = new DeprecationInfo(reasons, alternate, message);
        }

        int vulnerabilities = entry.TryGetProperty("vulnerabilities", out var v) && v.ValueKind == JsonValueKind.Array
            ? v.GetArrayLength()
            : 0;

        return new SearchMetadata(deprecation, vulnerabilities);
    }

    private readonly record struct ReleaseInfo(string Version, DateOnly Published, bool Listed);

    private async Task<ReleaseInfo?> FetchLatestReleaseAsync(string id, CancellationToken ct)
    {
        string url = $"{RegistrationApi}/{Uri.EscapeDataString(id.ToLowerInvariant())}/index.json";
        var root = await GetJsonAsync(url, ct);

        if (root is null
            || !root.Value.TryGetProperty("items", out var pages)
            || pages.GetArrayLength() == 0)
        {
            return null;
        }

        var lastPage = pages[pages.GetArrayLength() - 1];

        // Duze pakiety maja strony wyladowane osobno - wtedy trzeba pobrac strone po jej @id.
        if (!lastPage.TryGetProperty("items", out var entries) || entries.ValueKind != JsonValueKind.Array)
        {
            if (!lastPage.TryGetProperty("@id", out var pageUrl) || pageUrl.GetString() is not { } pageAddress)
            {
                return null;
            }

            var page = await GetJsonAsync(pageAddress, ct);
            if (page is null || !page.Value.TryGetProperty("items", out entries))
            {
                return null;
            }
        }

        if (entries.GetArrayLength() == 0)
        {
            return null;
        }

        var catalogEntry = entries[entries.GetArrayLength() - 1];
        if (!catalogEntry.TryGetProperty("catalogEntry", out var entry))
        {
            return null;
        }

        string version = entry.TryGetProperty("version", out var v) ? v.GetString() ?? "?" : "?";
        bool listed = !entry.TryGetProperty("listed", out var l) || l.ValueKind != JsonValueKind.False;

        if (!entry.TryGetProperty("published", out var p) || !p.TryGetDateTimeOffset(out var published))
        {
            return null;
        }

        // nuget.org oznacza pakiety wycofane z listy data 1900-01-01.
        if (published.Year <= 1901)
        {
            listed = false;
        }

        return new ReleaseInfo(version, DateOnly.FromDateTime(published.UtcDateTime), listed);
    }

    private async Task<JsonElement?> GetJsonAsync(string url, CancellationToken ct)
    {
        try
        {
            using var response = await _http.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

            return document.RootElement.Clone();
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or TaskCanceledException)
        {
            return null;
        }
    }

    /// <summary>Zbiera zaleznosci z plikow projektow oraz z centralnego Directory.Packages.props.</summary>
    private static List<PackageReference> CollectPackages(ScanContext ctx)
    {
        var files = ctx.ProjectFiles
            .Concat(ctx.Scanner.Find("**/Directory.Packages.props"))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        var packages = new Dictionary<string, PackageReference>(StringComparer.OrdinalIgnoreCase);

        foreach (string file in files)
        {
            XDocument document;
            try
            {
                document = XDocument.Load(file);
            }
            catch (Exception e) when (e is System.Xml.XmlException or IOException or UnauthorizedAccessException)
            {
                continue;
            }

            var elements = document.Descendants()
                .Where(e => e.Name.LocalName is "PackageReference" or "PackageVersion");

            foreach (var element in elements)
            {
                string? id = element.Attribute("Include")?.Value ?? element.Attribute("Update")?.Value;
                if (string.IsNullOrWhiteSpace(id) || id.Contains('$'))
                {
                    continue;
                }

                string? version = element.Attribute("Version")?.Value
                                  ?? element.Elements().FirstOrDefault(e => e.Name.LocalName == "Version")?.Value;

                packages.TryAdd(id, new PackageReference(id, version, ctx.Relative(file)));
            }
        }

        return [.. packages.Values];
    }

    private static string Join(params string?[] parts) =>
        string.Join(" · ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));

    private static string? Shorten(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        string single = System.Text.RegularExpressions.Regex.Replace(text.Trim(), @"\s+", " ");
        return single.Length <= 140 ? single : single[..137] + "…";
    }
}

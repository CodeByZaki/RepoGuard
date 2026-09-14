using System.Text.Json;
using Straznik.Core.Configuration;
using Straznik.Core.Dates;
using Straznik.Core.Model;
using Straznik.Core.Scanning;

namespace Straznik.Core.Inspectors;

/// <summary>
/// Feature flagi z data waznosci. Flaga mial byc tymczasowa, wdrozenie sie udalo,
/// a flaga zostala - razem z martwa galezia kodu po drugiej stronie ifa.
/// </summary>
public sealed class FeatureFlagInspector : SyncInspector
{
    public override string Id => "featureFlags";
    public override string DisplayName => "Feature flagi";

    public override bool IsEnabled(StraznikConfig config) => config.FeatureFlags.Enabled;

    private static readonly HashSet<string> ExpiryKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "expires", "expiresOn", "expiresAt", "expiry", "expiryDate", "expirationDate",
        "removeAfter", "removeBy", "retireOn", "sunset", "validUntil",
        "wygasa", "usunacPo", "terminUsuniecia",
    };

    private static readonly HashSet<string> EnabledKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "enabled", "isEnabled", "active", "isActive", "on", "value", "wlaczona", "włączona",
    };

    protected override IEnumerable<Finding> Inspect(ScanContext ctx, CancellationToken ct)
    {
        foreach (string file in ctx.Scanner.Find(ctx.Config.FeatureFlags.Files))
        {
            ct.ThrowIfCancellationRequested();

            string text;
            JsonDocument document;

            try
            {
                text = File.ReadAllText(file);
                document = JsonDocument.Parse(text, new JsonDocumentOptions
                {
                    CommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true,
                });
            }
            catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
            {
                // Uszkodzony albo niedostepny plik konfiguracji to nie jest zadanie tego inspektora.
                continue;
            }

            using (document)
            {
                var locator = new SequentialLocator(text);
                foreach (var finding in Walk(ctx, file, document.RootElement, string.Empty, locator))
                {
                    yield return finding;
                }
            }
        }
    }

    private IEnumerable<Finding> Walk(
        ScanContext ctx,
        string file,
        JsonElement element,
        string path,
        SequentialLocator locator)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var flag = TryReadFlag(element);
                if (flag is not null)
                {
                    var finding = Build(ctx, file, path, flag.Value, locator);
                    if (finding is not null)
                    {
                        yield return finding;
                    }
                }

                foreach (var property in element.EnumerateObject())
                {
                    foreach (var nested in Walk(ctx, file, property.Value, Append(path, property.Name), locator))
                    {
                        yield return nested;
                    }
                }

                break;

            case JsonValueKind.Array:
                int index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    foreach (var nested in Walk(ctx, file, item, $"{path}[{index}]", locator))
                    {
                        yield return nested;
                    }

                    index++;
                }

                break;
        }
    }

    private readonly record struct FlagInfo(string ExpiryKey, string RawExpiry, bool? Enabled, string? Owner);

    private static FlagInfo? TryReadFlag(JsonElement element)
    {
        string? expiryKey = null;
        string? rawExpiry = null;
        bool? enabled = null;
        string? owner = null;

        foreach (var property in element.EnumerateObject())
        {
            if (expiryKey is null && ExpiryKeys.Contains(property.Name) && property.Value.ValueKind == JsonValueKind.String)
            {
                expiryKey = property.Name;
                rawExpiry = property.Value.GetString();
            }
            else if (enabled is null && EnabledKeys.Contains(property.Name))
            {
                enabled = property.Value.ValueKind switch
                {
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    _ => null,
                };
            }
            else if (property.Name.Equals("owner", StringComparison.OrdinalIgnoreCase)
                     || property.Name.Equals("wlasciciel", StringComparison.OrdinalIgnoreCase))
            {
                owner = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : null;
            }
        }

        return expiryKey is not null && rawExpiry is not null
            ? new FlagInfo(expiryKey, rawExpiry, enabled, owner)
            : null;
    }

    private Finding? Build(ScanContext ctx, string file, string path, FlagInfo flag, SequentialLocator locator)
    {
        var found = DateHunter.Find(flag.RawExpiry);
        if (found is null)
        {
            return null;
        }

        var date = found.Value.Date;
        string name = path.Length == 0 ? "(korzeń)" : path;
        int line = locator.LineOf(flag.ExpiryKey);

        string state = flag.Enabled switch
        {
            true => "nadal włączona",
            false => "wyłączona",
            null => "stan nieokreślony",
        };

        var finding = ctx.Grader.WithDeadline(
            new Finding
            {
                Inspector = Id,
                Category = "Feature flaga",
                Title = $"{name} — {state}, termin {date:yyyy-MM-dd}",
                Location = $"{ctx.At(file, line)} → {name}",
                Detail = flag.Owner is null ? null : $"Właściciel: {flag.Owner}",
                Severity = Severity.Info,
                Hint = "Flaga po terminie to martwy kod po jednej ze stron ifa.",
            },
            date);

        // Flaga po terminie, ale juz wylaczona, to sprzatanie - nie pozar.
        return flag.Enabled == false && finding.Severity == Severity.Expired
            ? finding with
            {
                Severity = Severity.Notice,
                Hint = "Termin minął, flaga jest wyłączona — zostało usunięcie samej flagi i martwej gałęzi.",
            }
            : finding;
    }

    private static string Append(string path, string name) =>
        path.Length == 0 ? name : $"{path}/{name}";
}

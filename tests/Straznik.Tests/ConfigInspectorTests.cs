using System.Text;
using System.Text.Json;
using Straznik.Core.Model;

namespace Straznik.Tests;

public class FeatureFlagInspectorTests
{
    [Fact]
    public void Flaga_po_terminie_i_wciaz_wlaczona_to_pozar()
    {
        using var repo = new TempRepo().Write("appsettings.json", """
            {
              "FeatureFlags": {
                "nowyKoszyk": { "enabled": true, "expires": "2025-06-01", "owner": "checkout" }
              }
            }
            """);

        var finding = repo.Scan(today: "2026-09-13").Single("featureFlags");

        Assert.Equal(Severity.Expired, finding.Severity);
        Assert.Contains("nadal włączona", finding.Title);
        Assert.Contains("FeatureFlags/nowyKoszyk", finding.Location);
        Assert.Contains("checkout", finding.Detail);
    }

    [Fact]
    public void Flaga_po_terminie_ale_wylaczona_to_tylko_sprzatanie()
    {
        using var repo = new TempRepo().Write("appsettings.json", """
            {
              "FeatureFlags": {
                "staryRabat": { "enabled": false, "removeAfter": "2025-01-31" }
              }
            }
            """);

        var finding = repo.Scan(today: "2026-09-13").Single("featureFlags");

        Assert.Equal(Severity.Notice, finding.Severity);
        Assert.True(finding.IsOverdue);
    }

    [Fact]
    public void Flaga_bez_terminu_nie_jest_znaleziskiem()
    {
        using var repo = new TempRepo().Write("appsettings.json", """
            { "FeatureFlags": { "zwyklaFlaga": true } }
            """);

        Assert.Empty(repo.Scan().From("featureFlags"));
    }

    [Fact]
    public void Znajduje_flagi_zagniezdzone_w_tablicach()
    {
        using var repo = new TempRepo().Write("appsettings.json", """
            {
              "Features": [
                { "name": "a", "enabled": true, "validUntil": "2026-09-20" }
              ]
            }
            """);

        var finding = repo.Scan(today: "2026-09-13").Single("featureFlags");

        Assert.Equal(new DateOnly(2026, 9, 20), finding.Deadline);
        Assert.Contains("Features[0]", finding.Location);
    }
}

public class SecretExpiryInspectorTests
{
    [Fact]
    public void Czyta_date_waznosci_z_claimu_exp()
    {
        string token = Jwt(new DateOnly(2026, 9, 27), issuer: "sklep", subject: "magazyn");

        using var repo = new TempRepo().Write("appsettings.json", $$"""
            { "Integracje": { "Token": "{{token}}" } }
            """);

        var finding = repo.Scan(today: "2026-09-13").Single("secrets");

        Assert.Equal(new DateOnly(2026, 9, 27), finding.Deadline);
        Assert.Equal(14, finding.DaysLeft);
        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.Contains("sklep", finding.Detail);
    }

    [Fact]
    public void Nigdy_nie_pokazuje_calego_tokenu()
    {
        string token = Jwt(new DateOnly(2026, 12, 1));

        using var repo = new TempRepo().Write("appsettings.json", $$"""
            { "Token": "{{token}}" }
            """);

        var finding = repo.Scan(today: "2026-09-13").Single("secrets");
        string wszystko = $"{finding.Title} {finding.Detail} {finding.Location} {finding.Hint}";

        Assert.DoesNotContain(token, wszystko);
        Assert.Contains("…", finding.Title);
    }

    [Fact]
    public void Token_bez_exp_to_osobne_ostrzezenie()
    {
        string token = Jwt(expires: null, issuer: "wewnetrzny-wystawca");

        using var repo = new TempRepo().Write("appsettings.json", $$"""
            { "Token": "{{token}}" }
            """);

        var finding = repo.Scan().Single("secrets");

        Assert.Null(finding.Deadline);
        Assert.Equal(Severity.Notice, finding.Severity);
        Assert.Contains("bez daty wygaśnięcia", finding.Title);
    }

    /// <summary>Buduje atrape JWT - prawdziwy ksztalt, podpis bez znaczenia.</summary>
    private static string Jwt(DateOnly? expires, string? issuer = null, string? subject = null)
    {
        var payload = new Dictionary<string, object>();

        if (expires is { } date)
        {
            payload["exp"] = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)
                .ToUnixTimeSeconds();
        }

        if (issuer is not null)
        {
            payload["iss"] = issuer;
        }

        if (subject is not null)
        {
            payload["sub"] = subject;
        }

        return $"{Segment(new { alg = "HS256", typ = "JWT" })}.{Segment(payload)}.podpis-testowy";

        static string Segment(object value) => Convert
            .ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value)))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}

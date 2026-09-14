using System.Text;
using System.Text.Json;
using Straznik.Core.Model;

namespace Straznik.Core.Notifications;

/// <summary>
/// Ostatni krok Straznika: przekazanie wyniku czlowiekowi tam, gdzie ten czlowiek naprawde patrzy.
/// Format wiadomosci dobiera sie sam na podstawie adresu webhooka.
/// </summary>
public sealed class WebhookNotifier(HttpClient? httpClient = null)
{
    private readonly HttpClient _http = httpClient ?? new HttpClient();

    /// <summary>Ile znalezisk wchodzi do wiadomosci. Reszta zostaje w pelnym raporcie.</summary>
    private const int MaxItems = 8;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public async Task<bool> SendAsync(string webhookUrl, ScanResult result, CancellationToken ct = default)
    {
        object payload = Kind(webhookUrl) switch
        {
            WebhookKind.Discord => new { content = Message(result, limit: 1900) },
            WebhookKind.Slack => new { text = Message(result, limit: 3000) },
            _ => new { text = Message(result, limit: 3000), summary = Summary(result) },
        };

        // Serializujemy do tekstu, zamiast uzywac PostAsJsonAsync, zeby zadanie mialo
        // naglowek Content-Length. Przy strumieniowaniu HttpClient wysyla tresc jako
        // "Transfer-Encoding: chunked", a czesc odbiorcow webhookow (m.in. Discord)
        // odpowiada wtedy bledem 411 Length Required.
        string json = JsonSerializer.Serialize(payload, JsonOptions);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        try
        {
            using var response = await _http.PostAsync(webhookUrl, content, ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            return false;
        }
    }

    private enum WebhookKind
    {
        Generic,
        Discord,
        Slack,
    }

    private static WebhookKind Kind(string url)
    {
        if (url.Contains("discord.com/api/webhooks", StringComparison.OrdinalIgnoreCase)
            || url.Contains("discordapp.com/api/webhooks", StringComparison.OrdinalIgnoreCase))
        {
            return WebhookKind.Discord;
        }

        return url.Contains("hooks.slack.com", StringComparison.OrdinalIgnoreCase)
            ? WebhookKind.Slack
            : WebhookKind.Generic;
    }

    private static string Summary(ScanResult result) =>
        $"{result.Count(Severity.Expired)} po terminie, {result.Count(Severity.Warning)} wygasa wkrótce";

    /// <summary>
    /// Tresc wiadomosci. Cisza, gdy nie ma nic pilnego - powiadomienie, ktore przychodzi
    /// codziennie bez powodu, przestaje byc powiadomieniem.
    /// </summary>
    private static string Message(ScanResult result, int limit)
    {
        var sb = new StringBuilder();
        var urgent = result.Ordered
            .Where(f => f.Severity >= Severity.Warning)
            .Take(MaxItems)
            .ToList();

        if (urgent.Count == 0)
        {
            sb.AppendLine($"🛡️ **Strażnik Daty Ważności** — {result.Today:yyyy-MM-dd}");
            sb.Append("✅ Nic nie wygasa. Wszystkie terminy w repozytorium są pod kontrolą.");
            return sb.ToString();
        }

        sb.AppendLine($"🛡️ **Strażnik Daty Ważności** — {result.Today:yyyy-MM-dd}");
        sb.AppendLine($"🔴 {result.Count(Severity.Expired)} po terminie · "
                      + $"🟠 {result.Count(Severity.Warning)} wygasa w najbliższych dniach");
        sb.AppendLine();

        foreach (var finding in urgent)
        {
            string badge = finding.DaysLeft switch
            {
                null => "—",
                0 => "dziś",
                < 0 => $"{finding.DaysLeft} dni",
                _ => $"+{finding.DaysLeft} dni",
            };

            sb.AppendLine($"• `{badge}` {finding.Title}");
            sb.AppendLine($"   {finding.Location}");
        }

        int remaining = result.Findings.Count(f => f.Severity >= Severity.Warning) - urgent.Count;
        if (remaining > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"…i jeszcze {remaining}. Pełny raport w artefaktach przebiegu.");
        }

        string message = sb.ToString();
        return message.Length <= limit ? message : message[..(limit - 1)] + "…";
    }
}

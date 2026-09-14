using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using Straznik.Core.Configuration;
using Straznik.Core.Model;
using Straznik.Core.Scanning;

namespace Straznik.Core.Inspectors;

/// <summary>
/// Certyfikaty SSL adresow, z ktorych korzysta aplikacja. Adresy bierze z konfiguracji repo,
/// wiec lista pilnowanych domen aktualizuje sie sama razem z kodem.
/// </summary>
public sealed class TlsCertificateInspector : IInspector
{
    public string Id => "tls";
    public string DisplayName => "Certyfikaty SSL";

    public bool IsEnabled(StraznikConfig config) => config.Tls.Enabled;

    private static readonly Regex HttpsUrl = new(
        @"https://(?<host>[a-z0-9.-]+\.[a-z]{2,})(?::(?<port>\d+))?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Adresy, ktore nie sa prawdziwymi celami monitoringu.</summary>
    private static readonly string[] Ignored =
        ["localhost", "example.com", "example.org", "schemas.microsoft.com", "json.schemastore.org", "www.w3.org"];

    public async Task<IReadOnlyList<Finding>> InspectAsync(ScanContext ctx, CancellationToken ct)
    {
        var options = ctx.Config.Tls;
        var targets = CollectTargets(ctx, options);
        var findings = new List<Finding>();

        foreach (var (host, port) in targets)
        {
            ct.ThrowIfCancellationRequested();
            findings.Add(await CheckAsync(ctx, host, port, options.TimeoutSeconds, ct));
        }

        return findings;
    }

    private async Task<Finding> CheckAsync(ScanContext ctx, string host, int port, int timeoutSeconds, CancellationToken ct)
    {
        string target = port == 443 ? host : $"{host}:{port}";

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

            using var client = new TcpClient();
            await client.ConnectAsync(host, port, timeout.Token);

            X509Certificate2? captured = null;

            using var ssl = new SslStream(
                client.GetStream(),
                leaveInnerStreamOpen: false,
                userCertificateValidationCallback: (_, certificate, _, _) =>
                {
                    // Interesuje nas data waznosci, a nie zaufanie - lancuch moze byc dowolny,
                    // bo Straznik nie nawiazuje tu polaczenia, ktoremu mialby cokolwiek powierzyc.
                    if (certificate is not null)
                    {
                        captured = X509CertificateLoader.LoadCertificate(certificate.GetRawCertData());
                    }

                    return true;
                });

            await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = host }, timeout.Token);

            if (captured is null)
            {
                return Unavailable(ctx, target, "serwer nie przedstawił certyfikatu");
            }

            using (captured)
            {
                var notAfter = DateOnly.FromDateTime(captured.NotAfter);

                return ctx.Grader.WithDeadline(
                    new Finding
                    {
                        Inspector = Id,
                        Category = "SSL",
                        Title = $"{target} — certyfikat do {notAfter:yyyy-MM-dd}",
                        Location = target,
                        Detail = $"Wystawca: {Issuer(captured)}",
                        Severity = Severity.Info,
                        Hint = "Wygasły certyfikat to błąd, który widzi każdy użytkownik naraz.",
                    },
                    notAfter);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Unavailable(ctx, target, "przekroczono limit czasu");
        }
        catch (Exception e) when (e is SocketException or System.Security.Authentication.AuthenticationException or IOException)
        {
            return Unavailable(ctx, target, e.Message);
        }
    }

    private Finding Unavailable(ScanContext ctx, string target, string reason) => new()
    {
        Inspector = Id,
        Category = "SSL",
        Title = $"{target} — nie udało się sprawdzić certyfikatu",
        Location = target,
        Detail = reason,
        Severity = Severity.Info,
        Hint = "Sprawdź, czy adres jest nadal aktualny.",
    };

    private static string Issuer(X509Certificate2 certificate)
    {
        string issuer = certificate.Issuer;
        var match = Regex.Match(issuer, @"O=(?<org>[^,]+)");
        return match.Success ? match.Groups["org"].Value.Trim() : issuer;
    }

    private static List<(string Host, int Port)> CollectTargets(ScanContext ctx, TlsOptions options)
    {
        var targets = new Dictionary<string, (string Host, int Port)>(StringComparer.OrdinalIgnoreCase);

        foreach (string entry in options.Hosts)
        {
            var (host, port) = SplitHost(entry);
            targets[$"{host}:{port}"] = (host, port);
        }

        if (!options.DiscoverFromConfig)
        {
            return [.. targets.Values];
        }

        foreach (string file in ctx.Scanner.Find(ctx.Config.Secrets.Files))
        {
            string text;
            try
            {
                text = File.ReadAllText(file);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (Match match in HttpsUrl.Matches(text))
            {
                string host = match.Groups["host"].Value;

                if (Ignored.Any(i => host.Equals(i, StringComparison.OrdinalIgnoreCase)
                                     || host.EndsWith($".{i}", StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                int port = match.Groups["port"].Success ? int.Parse(match.Groups["port"].Value) : 443;
                targets[$"{host}:{port}"] = (host, port);
            }
        }

        return [.. targets.Values];
    }

    private static (string Host, int Port) SplitHost(string entry)
    {
        string value = entry.Replace("https://", string.Empty, StringComparison.OrdinalIgnoreCase).TrimEnd('/');
        int colon = value.IndexOf(':');

        return colon > 0 && int.TryParse(value[(colon + 1)..], out int port)
            ? (value[..colon], port)
            : (value, 443);
    }
}

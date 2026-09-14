using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Straznik.Core.Configuration;
using Straznik.Core.Model;
using Straznik.Core.Scanning;

namespace Straznik.Core.Inspectors;

/// <summary>
/// Tokeny i certyfikaty lezace w repozytorium maja wlasna date waznosci.
/// Straznik czyta ja z samego tokenu (claim exp) i z pliku certyfikatu (NotAfter) -
/// nigdzie nie zapisujac ani nie pokazujac samego sekretu.
/// </summary>
public sealed class SecretExpiryInspector : SyncInspector
{
    public override string Id => "secrets";
    public override string DisplayName => "Tokeny i certyfikaty";

    public override bool IsEnabled(StraznikConfig config) => config.Secrets.Enabled;

    /// <summary>Ksztalt JWT: naglowek.payload.podpis, oba pierwsze czlony zaczynaja sie od "eyJ".</summary>
    private static readonly Regex JwtShape = new(
        @"eyJ[A-Za-z0-9_-]{8,}\.eyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]*",
        RegexOptions.CultureInvariant);

    private static readonly string[] CertificatePatterns =
        ["**/*.pfx", "**/*.p12", "**/*.cer", "**/*.crt", "**/*.pem"];

    protected override IEnumerable<Finding> Inspect(ScanContext ctx, CancellationToken ct)
    {
        foreach (var finding in InspectTokens(ctx, ct))
        {
            yield return finding;
        }

        foreach (var finding in InspectCertificates(ctx, ct))
        {
            yield return finding;
        }
    }

    private IEnumerable<Finding> InspectTokens(ScanContext ctx, CancellationToken ct)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (string file in ctx.Scanner.Find(ctx.Config.Secrets.Files))
        {
            ct.ThrowIfCancellationRequested();

            string text;
            try
            {
                text = File.ReadAllText(file);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (Match match in JwtShape.Matches(text))
            {
                string token = match.Value;
                if (!seen.Add(token))
                {
                    continue;
                }

                var claims = ReadClaims(token);
                if (claims is null)
                {
                    continue;
                }

                int line = LineAt(text, match.Index);

                if (claims.Value.Expires is not { } expiry)
                {
                    yield return new Finding
                    {
                        Inspector = Id,
                        Category = "Token",
                        Title = $"Token {Mask(token)} bez daty wygaśnięcia",
                        Location = ctx.At(file, line),
                        Detail = Describe(claims.Value),
                        Severity = Severity.Notice,
                        Hint = "Token bez claimu exp nie wygaśnie sam. Jeśli wycieknie, jest ważny na zawsze.",
                    };

                    continue;
                }

                yield return ctx.Grader.WithDeadline(
                    new Finding
                    {
                        Inspector = Id,
                        Category = "Token",
                        Title = $"Token {Mask(token)} wygasa {expiry:yyyy-MM-dd}",
                        Location = ctx.At(file, line),
                        Detail = Describe(claims.Value),
                        Severity = Severity.Info,
                        Hint = "Wymień token, zanim przestanie działać w produkcji.",
                    },
                    expiry);
            }
        }
    }

    private IEnumerable<Finding> InspectCertificates(ScanContext ctx, CancellationToken ct)
    {
        foreach (string file in ctx.Scanner.Find(CertificatePatterns))
        {
            ct.ThrowIfCancellationRequested();

            var read = ReadCertificate(file);

            if (read.Skipped)
            {
                continue;
            }

            if (read.Error is not null)
            {
                yield return new Finding
                {
                    Inspector = Id,
                    Category = "Certyfikat",
                    Title = $"Nie udało się odczytać {Path.GetFileName(file)}",
                    Location = ctx.Relative(file),
                    Detail = "Plik jest zaszyfrowany hasłem albo uszkodzony — Strażnik nie sprawdzi jego ważności.",
                    Severity = Severity.Info,
                    Hint = "Sprawdź datę ważności ręcznie albo wskaż certyfikat w formie publicznej (.cer).",
                };

                continue;
            }

            var notAfter = read.NotAfter!.Value;
            string subject = read.Subject!;

            yield return ctx.Grader.WithDeadline(
                new Finding
                {
                    Inspector = Id,
                    Category = "Certyfikat",
                    Title = $"{Path.GetFileName(file)} wygasa {notAfter:yyyy-MM-dd}",
                    Location = ctx.Relative(file),
                    Detail = subject,
                    Severity = Severity.Info,
                    Hint = "Odnów certyfikat i podmień plik przed tą datą.",
                },
                notAfter);
        }
    }

    /// <summary>Wynik proby odczytania certyfikatu - bez wyjatkow przeciekajacych do iteratora.</summary>
    private readonly record struct CertificateRead(DateOnly? NotAfter, string? Subject, string? Error, bool Skipped);

    private static CertificateRead ReadCertificate(string file)
    {
        try
        {
            using var certificate = LoadCertificate(file);

            return certificate is null
                ? new CertificateRead(null, null, null, Skipped: true)
                : new CertificateRead(DateOnly.FromDateTime(certificate.NotAfter), certificate.Subject, null, false);
        }
        catch (Exception e) when (e is System.Security.Cryptography.CryptographicException
                                      or IOException
                                      or UnauthorizedAccessException)
        {
            return new CertificateRead(null, null, e.Message, false);
        }
    }

    private static X509Certificate2? LoadCertificate(string path)
    {
        string extension = Path.GetExtension(path).ToLowerInvariant();

        if (extension is ".pem")
        {
            string text = File.ReadAllText(path);
            return text.Contains("BEGIN CERTIFICATE", StringComparison.Ordinal)
                ? X509Certificate2.CreateFromPem(text)
                : null;
        }

        return extension is ".pfx" or ".p12"
            ? X509CertificateLoader.LoadPkcs12FromFile(path, password: null)
            : X509CertificateLoader.LoadCertificateFromFile(path);
    }

    private readonly record struct JwtClaims(DateOnly? Expires, string? Issuer, string? Subject);

    /// <summary>Czyta payload JWT. Nie weryfikuje podpisu - interesuje nas wylacznie data waznosci.</summary>
    private static JwtClaims? ReadClaims(string token)
    {
        string[] parts = token.Split('.');
        if (parts.Length < 2)
        {
            return null;
        }

        try
        {
            byte[] payload = DecodeBase64Url(parts[1]);
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;

            DateOnly? expires = root.TryGetProperty("exp", out var exp) && exp.TryGetInt64(out long seconds)
                ? DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime)
                : null;

            return new JwtClaims(
                expires,
                Text(root, "iss"),
                Text(root, "sub"));
        }
        catch (Exception e) when (e is FormatException or JsonException or ArgumentOutOfRangeException)
        {
            return null;
        }

        static string? Text(JsonElement root, string name) =>
            root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }

    private static byte[] DecodeBase64Url(string value)
    {
        string padded = value.Replace('-', '+').Replace('_', '/');
        padded += (padded.Length % 4) switch
        {
            2 => "==",
            3 => "=",
            0 => string.Empty,
            _ => throw new FormatException("Nieprawidłowa długość base64url."),
        };

        return Convert.FromBase64String(padded);
    }

    private static string Describe(JwtClaims claims)
    {
        var parts = new List<string>();

        if (claims.Issuer is not null)
        {
            parts.Add($"iss: {claims.Issuer}");
        }

        if (claims.Subject is not null)
        {
            parts.Add($"sub: {claims.Subject}");
        }

        return parts.Count == 0 ? "Token osadzony w konfiguracji." : string.Join(" · ", parts);
    }

    /// <summary>Pokazuje tyle tokenu, zeby dalo sie go znalezc, i tak malo, zeby nic nie wyciekalo.</summary>
    private static string Mask(string token) => $"{token[..Math.Min(10, token.Length)]}…";

    private static int LineAt(string text, int index)
    {
        int line = 1;
        var span = text.AsSpan(0, index);

        foreach (char c in span)
        {
            if (c == '\n')
            {
                line++;
            }
        }

        return line;
    }
}

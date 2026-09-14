using System.Text.Json;
using System.Text.Json.Serialization;

namespace Straznik.Core.Configuration;

public sealed class StraznikConfig
{
    /// <summary>Ile dni przed terminem Straznik ma zaczac krzyczec.</summary>
    public int WarnWithinDays { get; set; } = 30;

    /// <summary>Ile dni przed terminem sprawa ma trafic do kategorii "zbliza sie".</summary>
    public int NoticeWithinDays { get; set; } = 90;

    /// <summary>Katalogi i pliki pomijane przez wszystkie inspektory.</summary>
    public List<string> Exclude { get; set; } =
    [
        "**/bin/**",
        "**/obj/**",
        "**/node_modules/**",
        "**/.git/**",
        "**/*.Designer.cs",
        "**/Migrations/**",
    ];

    public DatedCommentsOptions DatedComments { get; set; } = new();
    public ObsoleteOptions Obsolete { get; set; } = new();
    public HardcodedDatesOptions HardcodedDates { get; set; } = new();
    public FeatureFlagsOptions FeatureFlags { get; set; } = new();
    public TargetFrameworkOptions TargetFramework { get; set; } = new();
    public SecretsOptions Secrets { get; set; } = new();
    public TlsOptions Tls { get; set; } = new();
    public NuGetOptions NuGet { get; set; } = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public static StraznikConfig Load(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return new StraznikConfig();
        }

        string json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<StraznikConfig>(json, JsonOptions)
               ?? throw new InvalidOperationException($"Nie udało się wczytać konfiguracji z '{path}'.");
    }

    public static string Serialize(StraznikConfig config) => JsonSerializer.Serialize(config, JsonOptions);
}

public sealed class DatedCommentsOptions
{
    public bool Enabled { get; set; } = true;

    /// <summary>Znaczniki, przy ktorych Straznik szuka daty.</summary>
    public List<string> Markers { get; set; } =
        ["TODO", "FIXME", "HACK", "XXX", "TEMP", "TYMCZASOWO", "DEADLINE", "REMOVE"];

    /// <summary>
    /// Zglaszac takze znaczniki bez daty? Domyslnie nie - w typowym repo sa ich setki
    /// i utopilyby prawdziwe terminy. Straznik i tak policzy je w podsumowaniu.
    /// </summary>
    public bool ReportUndated { get; set; }
}

public sealed class ObsoleteOptions
{
    public bool Enabled { get; set; } = true;

    /// <summary>Po ilu miesiacach od wycofania [Obsolete] uznajemy za zalegajace.</summary>
    public int StaleAfterMonths { get; set; } = 12;
}

public sealed class HardcodedDatesOptions
{
    public bool Enabled { get; set; } = true;
}

public sealed class FeatureFlagsOptions
{
    public bool Enabled { get; set; } = true;

    public List<string> Files { get; set; } =
    [
        "**/appsettings*.json",
        "**/*flags*.json",
        "**/*features*.json",
    ];
}

public sealed class TargetFrameworkOptions
{
    public bool Enabled { get; set; } = true;
}

public sealed class SecretsOptions
{
    public bool Enabled { get; set; } = true;

    public List<string> Files { get; set; } =
    [
        "**/appsettings*.json",
        "**/*.env",
        "**/.env",
        "**/.env.*",
        "**/*.config",
    ];
}

public sealed class TlsOptions
{
    /// <summary>Wymaga sieci, wiec domyslnie wylaczone.</summary>
    public bool Enabled { get; set; }

    /// <summary>Hosty sprawdzane zawsze, niezaleznie od tego, co jest w configach.</summary>
    public List<string> Hosts { get; set; } = [];

    /// <summary>Dociagnac adresy https:// znalezione w plikach konfiguracyjnych?</summary>
    public bool DiscoverFromConfig { get; set; } = true;

    public int TimeoutSeconds { get; set; } = 8;
}

public sealed class NuGetOptions
{
    /// <summary>Wymaga sieci, wiec domyslnie wylaczone.</summary>
    public bool Enabled { get; set; }

    /// <summary>Po ilu miesiacach bez nowego wydania pakiet uznajemy za porzucony.</summary>
    public int StaleAfterMonths { get; set; } = 24;

    public int TimeoutSeconds { get; set; } = 15;
}

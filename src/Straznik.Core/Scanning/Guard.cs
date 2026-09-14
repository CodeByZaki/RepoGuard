using System.Diagnostics;
using Straznik.Core.Configuration;
using Straznik.Core.Dates;
using Straznik.Core.Inspectors;
using Straznik.Core.Model;

namespace Straznik.Core.Scanning;

/// <summary>
/// Strażnik. Obchodzi repozytorium jednym przejsciem i zbiera wszystko, co ma date waznosci.
/// </summary>
public sealed class Guard(IReadOnlyList<IInspector>? inspectors = null)
{
    private readonly IReadOnlyList<IInspector> _inspectors = inspectors ?? Default();

    public static IReadOnlyList<IInspector> Default() =>
    [
        new DatedCommentInspector(),
        new ObsoleteInspector(),
        new HardcodedDateInspector(),
        new FeatureFlagInspector(),
        new TargetFrameworkInspector(),
        new SecretExpiryInspector(),
        new TlsCertificateInspector(),
        new NuGetInspector(),
    ];

    public async Task<ScanResult> RunAsync(
        string rootPath,
        StraznikConfig config,
        DateOnly today,
        CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();
        string root = Path.GetFullPath(rootPath);

        var scanner = new FileScanner(root, config.Exclude);
        var csharpFiles = scanner.Find("**/*.cs");
        var projectFiles = scanner.Find("**/*.csproj", "**/*.fsproj", "**/*.vbproj");

        var context = new ScanContext
        {
            RootPath = root,
            Config = config,
            Grader = new Grader(today, config.WarnWithinDays, config.NoticeWithinDays),
            Scanner = scanner,
            Syntax = new SyntaxCache(),
            CSharpFiles = csharpFiles,
            ProjectFiles = projectFiles,
        };

        var findings = new List<Finding>();
        var problems = new List<InspectorProblem>();
        var ran = new List<string>();
        int undatedMarkers = 0;

        foreach (var inspector in _inspectors)
        {
            ct.ThrowIfCancellationRequested();

            if (!inspector.IsEnabled(config))
            {
                continue;
            }

            try
            {
                findings.AddRange(await inspector.InspectAsync(context, ct));
                ran.Add(inspector.DisplayName);

                if (inspector is DatedCommentInspector dated)
                {
                    undatedMarkers = dated.UndatedCount;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception e)
            {
                // Jeden inspektor, ktoremu cos nie wyszlo, nie moze uciszyc calego Straznika.
                // Zglaszamy to jawnie - cichy monitoring jest gorszy niz zaden.
                problems.Add(new InspectorProblem(inspector.DisplayName, e.Message));
            }
        }

        stopwatch.Stop();

        return new ScanResult
        {
            RootPath = root,
            Today = today,
            Findings = Deduplicate(findings),
            RanInspectors = ran,
            Problems = problems,
            Stats = new ScanStats
            {
                CSharpFiles = csharpFiles.Count,
                ProjectFiles = projectFiles.Count,
                ConfigFiles = scanner.Find(config.Secrets.Files.Concat(config.FeatureFlags.Files)).Count,
                UndatedMarkers = undatedMarkers,
                Duration = stopwatch.Elapsed,
            },
        };
    }

    /// <summary>
    /// To samo znalezisko potrafi zlapac dwoch inspektorow (np. data w komentarzu nad
    /// wycofana metoda). Czlowiek ma zobaczyc je raz.
    /// </summary>
    private static List<Finding> Deduplicate(List<Finding> findings)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<Finding>(findings.Count);

        foreach (var finding in findings)
        {
            if (seen.Add(finding.Key))
            {
                result.Add(finding);
            }
        }

        return result;
    }
}

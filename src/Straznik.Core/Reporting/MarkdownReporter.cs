using System.Text;
using Straznik.Core.Model;
using Straznik.Core.Text;

namespace Straznik.Core.Reporting;

/// <summary>
/// Markdown do podsumowania zadania w GitHub Actions i do tresci zgloszenia (issue).
/// </summary>
public static class MarkdownReporter
{
    public static string Render(ScanResult result)
    {
        var sb = new StringBuilder();

        sb.AppendLine("## 🛡️ Strażnik Daty Ważności");
        sb.AppendLine();
        sb.AppendLine($"**Kontrola:** {result.Today:yyyy-MM-dd} · "
                      + $"{Plural.Files(result.Stats.CSharpFiles)} .cs · "
                      + $"{Plural.Projects(result.Stats.ProjectFiles)} · "
                      + $"{Plural.Inspectors(result.RanInspectors.Count)}");
        sb.AppendLine();

        if (result.Findings.Count == 0)
        {
            sb.AppendLine("✅ **Nic nie wygasa.** Żadna data w tym repozytorium nie jest po terminie.");
            AppendProblems(sb, result);
            return sb.ToString();
        }

        sb.AppendLine("| | Po terminie | Wygasa wkrótce | Zbliża się | Do wiadomości |");
        sb.AppendLine("|---|---:|---:|---:|---:|");
        sb.AppendLine($"| Liczba | {result.Count(Severity.Expired)} | {result.Count(Severity.Warning)} "
                      + $"| {result.Count(Severity.Notice)} | {result.Count(Severity.Info)} |");
        sb.AppendLine();

        foreach (var severity in new[] { Severity.Expired, Severity.Warning, Severity.Notice, Severity.Info })
        {
            AppendSection(sb, result, severity);
        }

        AppendProblems(sb, result);

        if (result.Stats.UndatedMarkers > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"> Pominięto {Plural.Markers(result.Stats.UndatedMarkers)} TODO/FIXME bez daty — "
                          + "Strażnik pilnuje tylko tych z terminem.");
        }

        return sb.ToString();
    }

    private static void AppendSection(StringBuilder sb, ScanResult result, Severity severity)
    {
        var items = result.Ordered.Where(f => f.Severity == severity).ToList();
        if (items.Count == 0)
        {
            return;
        }

        sb.AppendLine($"### {Icon(severity)} {severity.Label()} ({items.Count})");
        sb.AppendLine();
        sb.AppendLine("| Termin | Co | Gdzie |");
        sb.AppendLine("|---|---|---|");

        foreach (var finding in items)
        {
            sb.AppendLine($"| {Deadline(finding)} | {Escape(finding.Title)} | `{Escape(finding.Location)}` |");
        }

        sb.AppendLine();
    }

    private static void AppendProblems(StringBuilder sb, ScanResult result)
    {
        if (result.Problems.Count == 0)
        {
            return;
        }

        sb.AppendLine();
        sb.AppendLine("### ⚠️ Inspektorzy, którzy nie dokończyli");
        sb.AppendLine();

        foreach (var problem in result.Problems)
        {
            sb.AppendLine($"- **{Escape(problem.Inspector)}** — {Escape(problem.Message)}");
        }
    }

    private static string Deadline(Finding finding) => finding.DaysLeft switch
    {
        null => "—",
        0 => "**dziś**",
        < 0 => $"**{finding.DaysLeft} dni** ({finding.Deadline:yyyy-MM-dd})",
        _ => $"+{finding.DaysLeft} dni ({finding.Deadline:yyyy-MM-dd})",
    };

    private static string Icon(Severity severity) => severity switch
    {
        Severity.Expired => "🔴",
        Severity.Warning => "🟠",
        Severity.Notice => "🔵",
        _ => "⚪",
    };

    private static string Escape(string text) =>
        text.Replace("|", "\\|", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Replace("\r", string.Empty, StringComparison.Ordinal);
}

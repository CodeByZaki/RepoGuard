using System.Net;
using System.Text;
using Straznik.Core.Model;
using Straznik.Core.Text;

namespace Straznik.Core.Reporting;

/// <summary>
/// Samodzielny plik HTML - jeden artefakt, ktory mozna odlozyc w CI, wyslac mailem
/// albo otworzyc na telefonie. Zero zaleznosci zewnetrznych.
/// </summary>
public static class HtmlReporter
{
    public static string Render(ScanResult result)
    {
        var sb = new StringBuilder();

        sb.AppendLine("<!doctype html>");
        sb.AppendLine("<html lang=\"pl\"><head><meta charset=\"utf-8\">");
        sb.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        sb.AppendLine($"<title>Strażnik Daty Ważności — {result.Today:yyyy-MM-dd}</title>");
        sb.AppendLine($"<style>{Css}</style>");
        sb.AppendLine("</head><body>");

        Header(sb, result);
        Cards(sb, result);
        TimelineBlock(sb, result);

        if (result.Findings.Count == 0)
        {
            sb.AppendLine("<p class=\"clear\">Nic nie wygasa. Żadna data w tym repozytorium nie jest po terminie.</p>");
        }
        else
        {
            foreach (var severity in new[] { Severity.Expired, Severity.Warning, Severity.Notice, Severity.Info })
            {
                Section(sb, result, severity);
            }
        }

        Problems(sb, result);
        Footer(sb, result);

        sb.AppendLine("</body></html>");
        return sb.ToString();
    }

    private static void Header(StringBuilder sb, ScanResult result)
    {
        sb.AppendLine("<header>");
        sb.AppendLine("<h1>Strażnik Daty Ważności</h1>");
        sb.AppendLine("<p class=\"tagline\">Twój kod ma datę ważności. Nikt jej nie sprawdza.</p>");
        sb.AppendLine($"<p class=\"meta\">{E(Path.GetFileName(result.RootPath.TrimEnd(Path.DirectorySeparatorChar)))} "
                      + $"· kontrola {result.Today:yyyy-MM-dd} "
                      + $"· {Plural.Files(result.Stats.CSharpFiles)} .cs "
                      + $"· {Plural.Projects(result.Stats.ProjectFiles)} "
                      + $"· {Plural.Inspectors(result.RanInspectors.Count)}</p>");
        sb.AppendLine("</header>");
    }

    private static void Cards(StringBuilder sb, ScanResult result)
    {
        sb.AppendLine("<div class=\"cards\">");
        Card(sb, "po terminie", result.Count(Severity.Expired), "expired");
        Card(sb, "wygasa wkrótce", result.Count(Severity.Warning), "warning");
        Card(sb, "zbliża się", result.Count(Severity.Notice), "notice");
        Card(sb, "do wiadomości", result.Count(Severity.Info), "info");
        sb.AppendLine("</div>");
    }

    private static void Card(StringBuilder sb, string label, int count, string kind)
    {
        sb.AppendLine($"<div class=\"card {kind}\"><span class=\"num\">{count}</span><span class=\"lbl\">{E(label)}</span></div>");
    }

    private static void TimelineBlock(StringBuilder sb, ScanResult result)
    {
        var buckets = Timeline.Build(result);
        if (buckets.All(b => b.Count == 0))
        {
            return;
        }

        int max = Math.Max(1, buckets.Max(b => b.Count));

        sb.AppendLine("<section><h2>Oś czasu · najbliższe 12 miesięcy</h2>");
        sb.AppendLine("<div class=\"timeline\">");

        foreach (var bucket in buckets)
        {
            int height = bucket.Count == 0 ? 2 : Math.Max(6, bucket.Count * 100 / max);
            string kind = Kind(bucket.Worst);
            string title = $"{bucket.Label} {bucket.Year}: {bucket.Count}";

            sb.AppendLine($"<div class=\"tcol\" title=\"{E(title)}\">"
                          + $"<div class=\"tbar {kind}\" style=\"height:{height}%\"></div>"
                          + $"<span class=\"tnum\">{(bucket.Count == 0 ? "·" : bucket.Count.ToString())}</span>"
                          + $"<span class=\"tlbl\">{E(bucket.Label)}</span></div>");
        }

        sb.AppendLine("</div></section>");
    }

    private static void Section(StringBuilder sb, ScanResult result, Severity severity)
    {
        var items = result.Ordered.Where(f => f.Severity == severity).ToList();
        if (items.Count == 0)
        {
            return;
        }

        string kind = Kind(severity);

        sb.AppendLine($"<section><h2 class=\"{kind}\">{E(severity.Label())} <span class=\"count\">{items.Count}</span></h2>");
        sb.AppendLine("<ul class=\"findings\">");

        foreach (var finding in items)
        {
            sb.AppendLine($"<li class=\"{kind}\">");
            sb.AppendLine($"<div class=\"badge\">{E(Badge(finding))}</div>");
            sb.AppendLine("<div class=\"body\">");
            sb.AppendLine($"<div class=\"title\">{E(finding.Title)}</div>");
            sb.AppendLine($"<div class=\"loc\"><code>{E(finding.Location)}</code>"
                          + $"<span class=\"tag\">{E(finding.Category)}</span></div>");

            if (finding.Detail is { Length: > 0 } detail)
            {
                sb.AppendLine($"<div class=\"detail\">{E(detail)}</div>");
            }

            if (finding.Hint is { Length: > 0 } hint && severity >= Severity.Warning)
            {
                sb.AppendLine($"<div class=\"hint\">{E(hint)}</div>");
            }

            sb.AppendLine("</div></li>");
        }

        sb.AppendLine("</ul></section>");
    }

    private static void Problems(StringBuilder sb, ScanResult result)
    {
        if (result.Problems.Count == 0)
        {
            return;
        }

        sb.AppendLine("<section><h2 class=\"warning\">Inspektorzy, którzy nie dokończyli</h2><ul class=\"plain\">");

        foreach (var problem in result.Problems)
        {
            sb.AppendLine($"<li><strong>{E(problem.Inspector)}</strong> — {E(problem.Message)}</li>");
        }

        sb.AppendLine("</ul></section>");
    }

    private static void Footer(StringBuilder sb, ScanResult result)
    {
        sb.AppendLine("<footer>");

        if (result.Stats.UndatedMarkers > 0)
        {
            sb.AppendLine($"<p>Pominięto {result.Stats.UndatedMarkers} znaczników TODO/FIXME bez daty — "
                          + "Strażnik pilnuje tylko tych z terminem.</p>");
        }

        sb.AppendLine("<p>Raport wygenerowany automatycznie przez Strażnika Daty Ważności.</p>");
        sb.AppendLine("</footer>");
    }

    private static string Badge(Finding finding) => finding.DaysLeft switch
    {
        null => "—",
        0 => "dziś",
        < 0 => $"{finding.DaysLeft} dni",
        _ => $"+{finding.DaysLeft} dni",
    };

    private static string Kind(Severity severity) => severity switch
    {
        Severity.Expired => "expired",
        Severity.Warning => "warning",
        Severity.Notice => "notice",
        _ => "info",
    };

    private static string E(string? text) => WebUtility.HtmlEncode(text ?? string.Empty);

    private const string Css = """
        :root {
          --bg: #f7f7f5; --fg: #1b1b1a; --muted: #6b6b66; --line: #e2e2dd; --panel: #ffffff;
          --expired: #c8342b; --warning: #b46a00; --notice: #1f6f9c; --info: #8a8a84;
        }
        @media (prefers-color-scheme: dark) {
          :root {
            --bg: #16161a; --fg: #ececea; --muted: #9a9a94; --line: #2c2c32; --panel: #1e1e23;
            --expired: #ff6b5e; --warning: #f0a02a; --notice: #5cb3e0; --info: #8a8a84;
          }
        }
        * { box-sizing: border-box; }
        body {
          margin: 0; padding: 32px 20px 64px; background: var(--bg); color: var(--fg);
          font: 15px/1.55 ui-sans-serif, -apple-system, "Segoe UI", Roboto, sans-serif;
        }
        header, section, footer { max-width: 900px; margin: 0 auto; }
        h1 { font-size: 26px; margin: 0 0 4px; letter-spacing: -0.01em; }
        .tagline { margin: 0 0 10px; color: var(--muted); font-size: 16px; }
        .meta { margin: 0 0 26px; color: var(--muted); font-size: 13px; }
        .cards { max-width: 900px; margin: 0 auto 30px; display: grid; gap: 12px;
                 grid-template-columns: repeat(auto-fit, minmax(150px, 1fr)); }
        .card { background: var(--panel); border: 1px solid var(--line); border-radius: 10px;
                padding: 14px 16px; display: flex; flex-direction: column; gap: 2px; }
        .card .num { font-size: 30px; font-weight: 650; line-height: 1; }
        .card .lbl { color: var(--muted); font-size: 12.5px; }
        .card.expired .num { color: var(--expired); }
        .card.warning .num { color: var(--warning); }
        .card.notice .num { color: var(--notice); }
        .card.info .num { color: var(--info); }
        h2 { font-size: 15px; text-transform: uppercase; letter-spacing: 0.07em;
             margin: 34px 0 12px; padding-bottom: 8px; border-bottom: 1px solid var(--line); }
        h2.expired { color: var(--expired); } h2.warning { color: var(--warning); }
        h2.notice { color: var(--notice); } h2.info { color: var(--info); }
        h2 .count { color: var(--muted); font-weight: 400; }
        .timeline { display: flex; align-items: flex-end; gap: 6px; height: 150px;
                    background: var(--panel); border: 1px solid var(--line);
                    border-radius: 10px; padding: 14px 12px 8px; }
        .tcol { flex: 1; display: flex; flex-direction: column; align-items: center;
                justify-content: flex-end; height: 100%; gap: 4px; }
        .tbar { width: 100%; max-width: 34px; border-radius: 4px 4px 2px 2px; background: var(--info); }
        .tbar.expired { background: var(--expired); } .tbar.warning { background: var(--warning); }
        .tbar.notice { background: var(--notice); }
        .tnum { font-size: 11.5px; color: var(--muted); } .tlbl { font-size: 11.5px; color: var(--muted); }
        ul.findings { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; gap: 8px; }
        ul.findings li { display: flex; gap: 14px; background: var(--panel); border: 1px solid var(--line);
                         border-left: 3px solid var(--info); border-radius: 8px; padding: 12px 14px; }
        ul.findings li.expired { border-left-color: var(--expired); }
        ul.findings li.warning { border-left-color: var(--warning); }
        ul.findings li.notice { border-left-color: var(--notice); }
        .badge { flex: 0 0 84px; font-variant-numeric: tabular-nums; font-weight: 600; font-size: 13.5px; }
        li.expired .badge { color: var(--expired); } li.warning .badge { color: var(--warning); }
        li.notice .badge { color: var(--notice); } li.info .badge { color: var(--info); }
        .body { min-width: 0; }
        .title { font-weight: 550; margin-bottom: 3px; overflow-wrap: anywhere; }
        .loc { font-size: 12.5px; color: var(--muted); display: flex; gap: 8px;
               flex-wrap: wrap; align-items: center; }
        .loc code { font: 12px ui-monospace, "Cascadia Code", Consolas, monospace; overflow-wrap: anywhere; }
        .tag { border: 1px solid var(--line); border-radius: 20px; padding: 1px 8px; font-size: 11px; }
        .detail { font-size: 13px; color: var(--muted); margin-top: 5px; overflow-wrap: anywhere; }
        .hint { font-size: 13px; margin-top: 6px; opacity: 0.95; }
        .hint::before { content: "↳ "; }
        ul.plain { color: var(--muted); font-size: 13.5px; }
        .clear { max-width: 900px; margin: 0 auto; padding: 18px; background: var(--panel);
                 border: 1px solid var(--line); border-radius: 10px; }
        footer { margin-top: 40px; padding-top: 16px; border-top: 1px solid var(--line);
                 color: var(--muted); font-size: 12.5px; }
        footer p { margin: 4px 0; }
        """;
}

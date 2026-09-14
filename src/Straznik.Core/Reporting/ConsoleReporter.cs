using System.Globalization;
using System.Text;
using Straznik.Core.Model;
using Straznik.Core.Text;

namespace Straznik.Core.Reporting;

public sealed record ConsoleStyle(bool Color, bool Unicode)
{
    public static ConsoleStyle Rich => new(true, true);
    public static ConsoleStyle Plain => new(false, false);
}

/// <summary>
/// Raport dla czlowieka patrzacego w terminal. Najpilniejsze na gorze,
/// os czasu na dole, zero scrollowania w poszukiwaniu sedna.
/// </summary>
public sealed class ConsoleReporter(ConsoleStyle style)
{
    private const int Width = 78;

    private readonly bool _color = style.Color;
    private readonly bool _unicode = style.Unicode;

    // "\e" to sekwencja ucieczki ESC dodana w C# 13 - czytelniejsza niz "".
    private const string Reset = "\e[0m";
    private const string Dim = "\e[90m";
    private const string Bold = "\e[1m";
    private const string Red = "\e[91m";
    private const string Yellow = "\e[93m";
    private const string Cyan = "\e[96m";
    private const string Green = "\e[92m";
    private const string Grey = "\e[37m";

    public string Render(ScanResult result)
    {
        var sb = new StringBuilder();

        Header(sb);
        Meta(sb, result);

        if (result.Findings.Count == 0)
        {
            AllClear(sb, result);
        }
        else
        {
            foreach (var severity in new[] { Severity.Expired, Severity.Warning, Severity.Notice, Severity.Info })
            {
                Section(sb, result, severity);
            }

            TimelineBlock(sb, result);
        }

        Problems(sb, result);
        Summary(sb, result);

        return sb.ToString();
    }

    private void Header(StringBuilder sb)
    {
        const string title = "S T R A Ż N I K   D A T Y   W A Ż N O Ś C I";
        const string tagline = "Twój kod ma datę ważności. Nikt jej nie sprawdza.";

        sb.AppendLine();
        sb.AppendLine(Paint(Box(Top), Cyan));
        sb.AppendLine(Paint(BoxLine(title), Cyan + Bold));
        sb.AppendLine(Paint(BoxLine(tagline), Cyan));
        sb.AppendLine(Paint(Box(Bottom), Cyan));
        sb.AppendLine();
    }

    private void Meta(StringBuilder sb, ScanResult result)
    {
        var stats = result.Stats;
        string seconds = stats.Duration.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture);

        Field(sb, "Repozytorium", Path.GetFileName(result.RootPath.TrimEnd(Path.DirectorySeparatorChar)));
        Field(sb, "Kontrola", $"{result.Today:yyyy-MM-dd} · {Plural.Inspectors(result.RanInspectors.Count)} · {seconds} s");
        Field(sb, "Przejrzano", $"{Plural.Files(stats.CSharpFiles)} .cs · {Plural.Projects(stats.ProjectFiles)} "
                                + $"· {Plural.Files(stats.ConfigFiles)} konfiguracji");
        sb.AppendLine();
    }

    private void Field(StringBuilder sb, string label, string value)
    {
        sb.Append("  ").Append(Paint(label.PadRight(14), Dim)).AppendLine(value);
    }

    private void Section(StringBuilder sb, ScanResult result, Severity severity)
    {
        var items = result.Ordered.Where(f => f.Severity == severity).ToList();
        if (items.Count == 0)
        {
            return;
        }

        string color = ColorOf(severity);

        sb.Append("  ")
          .Append(Paint($"{GlyphOf(severity)}  {severity.Label()}", color + Bold))
          .AppendLine(Paint($" · {items.Count}", Dim));

        sb.Append("  ").AppendLine(Paint(new string(_unicode ? '─' : '-', Width - 2), Dim));

        foreach (var finding in items)
        {
            Entry(sb, finding, color, severity);
        }

        sb.AppendLine();
    }

    private void Entry(StringBuilder sb, Finding finding, string color, Severity severity)
    {
        string badge = Badge(finding).PadLeft(10);

        sb.Append("  ")
          .Append(Paint(badge, color + Bold))
          .Append("  ")
          .AppendLine(Wrap(finding.Title, 14));

        sb.Append(new string(' ', 14))
          .Append(Paint(finding.Location, Grey));

        if (finding.Category.Length > 0)
        {
            sb.Append(Paint($"  [{finding.Category}]", Dim));
        }

        sb.AppendLine();

        if (finding.Detail is { Length: > 0 } detail)
        {
            sb.Append(new string(' ', 14)).AppendLine(Paint(Wrap(detail, 14), Dim));
        }

        // Podpowiedz pokazujemy tylko tam, gdzie czlowiek ma cos zrobic teraz.
        if (severity >= Severity.Warning && finding.Hint is { Length: > 0 } hint)
        {
            sb.Append(new string(' ', 12))
              .Append(Paint(_unicode ? "↳ " : "> ", color))
              .AppendLine(Paint(Wrap(hint, 14), Dim));
        }

        sb.AppendLine();
    }

    private string Badge(Finding finding) => finding.DaysLeft switch
    {
        null => _unicode ? "—" : "-",
        0 => "dziś",
        < 0 => $"{finding.DaysLeft} dni",
        _ => $"+{finding.DaysLeft} dni",
    };

    private void TimelineBlock(StringBuilder sb, ScanResult result)
    {
        var buckets = Timeline.Build(result);
        if (buckets.All(b => b.Count == 0))
        {
            return;
        }

        sb.Append("  ").AppendLine(Paint("OŚ CZASU · najbliższe 12 miesięcy", Bold));
        sb.Append("  ").AppendLine(Paint(new string(_unicode ? '─' : '-', Width - 2), Dim));

        var labels = new StringBuilder("  ");
        var bars = new StringBuilder("  ");
        var counts = new StringBuilder("  ");

        foreach (var bucket in buckets)
        {
            labels.Append(Paint(bucket.Label.PadRight(6), bucket.Month == result.Today.Month ? Bold : Dim));
            bars.Append(Paint(Bar(bucket).PadRight(6), ColorOf(bucket.Worst)));
            counts.Append(Paint((bucket.Count == 0 ? "·" : bucket.Count.ToString()).PadRight(6), Dim));
        }

        sb.AppendLine(labels.ToString().TrimEnd());
        sb.AppendLine(bars.ToString().TrimEnd());
        sb.AppendLine(counts.ToString().TrimEnd());
        sb.AppendLine();
    }

    private string Bar(TimelineBucket bucket)
    {
        if (bucket.Count == 0)
        {
            return _unicode ? "·" : ".";
        }

        int height = Math.Min(bucket.Count, 4);
        return _unicode ? new string('█', height) : new string('#', height);
    }

    private void AllClear(StringBuilder sb, ScanResult result)
    {
        sb.Append("  ")
          .Append(Paint(_unicode ? "✔" : "OK", Green + Bold))
          .AppendLine(Paint("  Nic nie wygasa. Żadna data w tym repozytorium nie jest po terminie.", Green));

        sb.AppendLine();

        if (result.Stats.UndatedMarkers > 0)
        {
            sb.Append("  ")
              .AppendLine(Paint($"Uwaga: {Plural.Markers(result.Stats.UndatedMarkers)} TODO/FIXME bez daty — Strażnik ich nie pilnuje.", Dim));
            sb.AppendLine();
        }
    }

    private void Problems(StringBuilder sb, ScanResult result)
    {
        if (result.Problems.Count == 0)
        {
            return;
        }

        sb.Append("  ").AppendLine(Paint("INSPEKTORZY, KTÓRZY NIE DOKOŃCZYLI", Yellow + Bold));

        foreach (var problem in result.Problems)
        {
            sb.Append("    ").Append(Paint(problem.Inspector, Grey)).Append(" — ").AppendLine(Paint(problem.Message, Dim));
        }

        sb.AppendLine();
    }

    private void Summary(StringBuilder sb, ScanResult result)
    {
        sb.Append("  ").AppendLine(Paint(new string(_unicode ? '═' : '=', Width - 2), Dim));

        string counts = string.Join(
            Paint(" · ", Dim),
            Paint($"{result.Count(Severity.Expired)} po terminie", Red),
            Paint($"{result.Count(Severity.Warning)} wygasa wkrótce", Yellow),
            Paint($"{result.Count(Severity.Notice)} zbliża się", Cyan),
            Paint($"{result.Count(Severity.Info)} do wiadomości", Grey));

        sb.Append("  ").Append(Paint("Podsumowanie  ", Dim)).AppendLine(counts);

        if (result.Stats.UndatedMarkers > 0 && result.Findings.Count > 0)
        {
            sb.Append("  ")
              .Append(Paint("Pominięto     ", Dim))
              .AppendLine(Paint($"{Plural.Markers(result.Stats.UndatedMarkers)} TODO/FIXME bez daty", Dim));
        }

        sb.AppendLine();
    }

    private static string ColorOf(Severity severity) => severity switch
    {
        Severity.Expired => Red,
        Severity.Warning => Yellow,
        Severity.Notice => Cyan,
        _ => Grey,
    };

    private string GlyphOf(Severity severity)
    {
        if (!_unicode)
        {
            return severity switch
            {
                Severity.Expired => "[!]",
                Severity.Warning => "[^]",
                Severity.Notice => "[o]",
                _ => "[.]",
            };
        }

        return severity switch
        {
            Severity.Expired => "✖",
            Severity.Warning => "▲",
            Severity.Notice => "●",
            _ => "○",
        };
    }

    private string Paint(string text, string ansi) => _color ? $"{ansi}{text}{Reset}" : text;

    private char Top => _unicode ? '╭' : '+';
    private char Bottom => _unicode ? '╰' : '+';

    private string Box(char corner)
    {
        char horizontal = _unicode ? '─' : '-';
        char right = corner == Top
            ? (_unicode ? '╮' : '+')
            : (_unicode ? '╯' : '+');

        return $"  {corner}{new string(horizontal, Width - 4)}{right}";
    }

    private string BoxLine(string text)
    {
        char vertical = _unicode ? '│' : '|';
        int inner = Width - 4;
        string padded = text.Length > inner - 2 ? text[..(inner - 2)] : text;

        return $"  {vertical} {padded.PadRight(inner - 2)} {vertical}";
    }

    /// <summary>Zawija dlugi tekst tak, zeby kolejne linie trzymaly sie wciecia kolumny.</summary>
    private static string Wrap(string text, int indent)
    {
        int available = Width - indent;
        if (text.Length <= available)
        {
            return text;
        }

        var sb = new StringBuilder();
        int lineLength = 0;

        foreach (string word in text.Split(' '))
        {
            if (lineLength + word.Length + 1 > available && lineLength > 0)
            {
                sb.AppendLine().Append(new string(' ', indent));
                lineLength = 0;
            }

            if (lineLength > 0)
            {
                sb.Append(' ');
                lineLength++;
            }

            sb.Append(word);
            lineLength += word.Length;
        }

        return sb.ToString();
    }
}

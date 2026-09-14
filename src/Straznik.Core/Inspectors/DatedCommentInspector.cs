using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Straznik.Core.Configuration;
using Straznik.Core.Dates;
using Straznik.Core.Model;
using Straznik.Core.Scanning;
using Straznik.Core.Text;

namespace Straznik.Core.Inspectors;

/// <summary>
/// Szuka w komentarzach znacznikow z terminem: TODO(2026-03-01), HACK do konca marca 2026. (straznik:ignore)
/// To najczestsza data waznosci w kodzie i jednoczesnie jedyna, ktorej nikt nigdy nie pilnuje.
/// </summary>
public sealed class DatedCommentInspector : SyncInspector
{
    public override string Id => "datedComments";
    public override string DisplayName => "Datowane komentarze";

    /// <summary>
    /// Ile znacznikow bez daty Straznik minal po drodze. Nie zglaszamy ich jako znaleziska
    /// (w typowym repo sa ich setki), ale skala sama w sobie jest informacja.
    /// </summary>
    public int UndatedCount { get; private set; }

    public override bool IsEnabled(StraznikConfig config) => config.DatedComments.Enabled;

    protected override IEnumerable<Finding> Inspect(ScanContext ctx, CancellationToken ct)
    {
        UndatedCount = 0;

        var options = ctx.Config.DatedComments;
        var markerRegex = BuildMarkerRegex(options.Markers);

        foreach (string file in ctx.CSharpFiles)
        {
            ct.ThrowIfCancellationRequested();

            var tree = ctx.Syntax.Get(file);
            if (tree is null)
            {
                continue;
            }

            foreach (var block in CommentBlocks(tree, ct))
            {
                foreach (var finding in InspectBlock(ctx, file, block, markerRegex, options))
                {
                    yield return finding;
                }
            }
        }
    }

    private IEnumerable<Finding> InspectBlock(
        ScanContext ctx,
        string file,
        CommentBlock block,
        Regex markerRegex,
        DatedCommentsOptions options)
    {
        // Termin z sasiedniej linii wolno przypisac tylko wtedy, gdy w calym bloku jest
        // jeden znacznik. Przy kilku nie da sie zgadnac, do ktorego z nich nalezy data -
        // a zgadywanie zamienia raport w zbior falszywych alarmow.
        bool blockDateAllowed = block.Lines.Count(l => markerRegex.IsMatch(l.Text)) == 1;

        foreach (var (line, text) in block.Lines)
        {
            var marker = markerRegex.Match(text);
            if (!marker.Success || IgnoreMarker.IsIgnored(text))
            {
                continue;
            }

            // Data bywa w tej samej linii co znacznik, ale rownie czesto w nastepnej -
            // mysl w pierwszym wierszu, termin w drugim. Dlatego szukamy najpierw w linii,
            // a dopiero potem w calym bloku komentarza.
            var found = DateHunter.Find(text)
                        ?? (blockDateAllowed ? DateHunter.Find(block.Text) : null);

            if (found is null)
            {
                UndatedCount++;

                if (!options.ReportUndated)
                {
                    continue;
                }

                yield return new Finding
                {
                    Inspector = Id,
                    Category = marker.Value.ToUpperInvariant(),
                    Title = Shorten(text),
                    Location = ctx.At(file, line),
                    Detail = "Znacznik bez terminu.",
                    Severity = Severity.Info,
                    Hint = "Dopisz date w nawiasie, np. TODO(2026-03-01), a Strażnik zacznie tego pilnować.",
                };

                continue;
            }

            var value = found.Value;

            yield return ctx.Grader.WithDeadline(
                new Finding
                {
                    Inspector = Id,
                    Category = marker.Value.ToUpperInvariant(),
                    Title = Shorten(text),
                    Location = ctx.At(file, line),
                    Detail = DescribeDate(value),
                    Severity = Severity.Info,
                    Hint = "Zrób to, co obiecuje komentarz, albo świadomie przesuń termin.",
                },
                value.Date);
        }
    }

    private static string DescribeDate(FoundDate found) => found.Precision switch
    {
        DatePrecision.Month => $"Termin z komentarza: „{found.Matched}” → koniec miesiąca ({found.Date:yyyy-MM-dd}).",
        DatePrecision.Quarter => $"Termin z komentarza: „{found.Matched}” → koniec kwartału ({found.Date:yyyy-MM-dd}).",
        _ => $"Termin z komentarza: „{found.Matched}”.",
    };

    private static Regex BuildMarkerRegex(IEnumerable<string> markers)
    {
        string alternatives = string.Join('|', markers.Select(Regex.Escape));
        return new Regex(
            $@"\b(?:{alternatives})\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static string Shorten(string text)
    {
        string single = Regex.Replace(text.Trim(), @"\s+", " ");
        return single.Length <= 110 ? single : single[..107] + "…";
    }

    private sealed record CommentBlock(IReadOnlyList<(int Line, string Text)> Lines, string Text);

    /// <summary>
    /// Zbiera komentarze w bloki. Sasiadujace komentarze jednoliniowe traktujemy jak jedna
    /// wypowiedz, bo ludzie tak wlasnie pisza - mysl w pierwszej linii, termin w drugiej.
    /// </summary>
    private static IEnumerable<CommentBlock> CommentBlocks(SyntaxTree tree, CancellationToken ct)
    {
        var pending = new List<(int Line, string Text)>();
        int previousLine = int.MinValue;

        foreach (var trivia in tree.GetRoot(ct).DescendantTrivia())
        {
            if (!IsComment(trivia))
            {
                continue;
            }

            var span = trivia.GetLocation().GetLineSpan();
            int startLine = span.StartLinePosition.Line + 1;
            bool singleLine = trivia.IsKind(SyntaxKind.SingleLineCommentTrivia)
                              || trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia);

            if (!singleLine || startLine != previousLine + 1)
            {
                if (pending.Count > 0)
                {
                    yield return Build(pending);
                    pending = [];
                }
            }

            var lines = trivia.ToFullString().Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string clean = Clean(lines[i]);
                if (clean.Length > 0)
                {
                    pending.Add((startLine + i, clean));
                }
            }

            previousLine = span.EndLinePosition.Line + 1;

            if (!singleLine)
            {
                yield return Build(pending);
                pending = [];
                previousLine = int.MinValue;
            }
        }

        if (pending.Count > 0)
        {
            yield return Build(pending);
        }

        static CommentBlock Build(List<(int Line, string Text)> lines)
        {
            var sb = new StringBuilder();
            foreach (var (_, text) in lines)
            {
                sb.Append(text).Append(' ');
            }

            return new CommentBlock([.. lines], sb.ToString().Trim());
        }
    }

    private static bool IsComment(SyntaxTrivia trivia) =>
        trivia.IsKind(SyntaxKind.SingleLineCommentTrivia)
        || trivia.IsKind(SyntaxKind.MultiLineCommentTrivia)
        || trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia)
        || trivia.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia);

    /// <summary>Zdejmuje z linii komentarza jej "opakowanie": //, ///, /*, */, wiodace gwiazdki.</summary>
    private static string Clean(string line)
    {
        string s = line.Trim().TrimEnd('\r');

        if (s.StartsWith("/*", StringComparison.Ordinal))
        {
            s = s[2..];
        }

        if (s.EndsWith("*/", StringComparison.Ordinal))
        {
            s = s[..^2];
        }

        s = s.TrimStart();

        while (s.StartsWith('/'))
        {
            s = s[1..];
        }

        if (s.StartsWith('*'))
        {
            s = s[1..];
        }

        return s.Trim();
    }
}

using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Straznik.Core.Configuration;
using Straznik.Core.Model;
using Straznik.Core.Scanning;
using Straznik.Core.Text;

namespace Straznik.Core.Inspectors;

/// <summary>
/// Znajduje daty wpisane na sztywno w kod - bomby zegarowe.
/// Kod, ktory zachowa sie inaczej w konkretnym dniu, a nikt tego dnia nie ma w kalendarzu.
/// </summary>
public sealed class HardcodedDateInspector : SyncInspector
{
    public override string Id => "hardcodedDates";
    public override string DisplayName => "Daty w kodzie";

    public override bool IsEnabled(StraznikConfig config) => config.HardcodedDates.Enabled;

    /// <summary>Typy, ktorych konstruktor (rok, miesiac, dzien) nas interesuje.</summary>
    private static readonly HashSet<string> DateTypes = new(StringComparer.Ordinal)
    {
        "DateTime", "DateOnly", "DateTimeOffset",
    };

    /// <summary>
    /// Nazwy sugerujace, ze data jest granica waznosci. Dzieki nim zglaszamy tez daty
    /// z przeszlosci - bo minieta granica to wlasnie ten przypadek, o ktorym dowiadujesz sie za pozno.
    /// </summary>
    private static readonly Regex ExpiryName = new(
        @"expir|expiry|valid|until|deadline|cut ?off|end|koniec|termin|wygas|wa[żz]no|promo|trial|sunset|migrat",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex IsoLiteral = new(
        @"^\s*(\d{4})-(\d{2})-(\d{2})",
        RegexOptions.CultureInvariant);

    protected override IEnumerable<Finding> Inspect(ScanContext ctx, CancellationToken ct)
    {
        foreach (string file in ctx.CSharpFiles)
        {
            ct.ThrowIfCancellationRequested();

            var tree = ctx.Syntax.Get(file);
            if (tree is null)
            {
                continue;
            }

            var root = tree.GetRoot(ct);

            foreach (var node in root.DescendantNodes())
            {
                var hit = node switch
                {
                    ObjectCreationExpressionSyntax creation => FromConstructor(creation.Type.ToString(), creation.ArgumentList),
                    ImplicitObjectCreationExpressionSyntax implicitCreation => FromImplicitConstructor(implicitCreation),
                    InvocationExpressionSyntax invocation => FromParse(invocation),
                    _ => null,
                };

                if (hit is null)
                {
                    continue;
                }

                var finding = Evaluate(ctx, file, tree, node, hit.Value);
                if (finding is not null)
                {
                    yield return finding;
                }
            }
        }
    }

    private Finding? Evaluate(ScanContext ctx, string file, SyntaxTree tree, SyntaxNode node, DateOnly date)
    {
        // Wartosci wartownicze (1900-01-01, 9999-12-31) to nie terminy, tylko "brak wartosci".
        if (date.Year >= 2100 || date.Year <= ctx.Today.Year - 10)
        {
            return null;
        }

        // Data w inicjalizatorze kolekcji to dane referencyjne - tablica dat konca wsparcia,
        // slownik swiat, zestaw stawek. Kod sie tego dnia nie zmieni, wiec to nie bomba zegarowa.
        if (node.Ancestors().OfType<InitializerExpressionSyntax>().Any())
        {
            return null;
        }

        string owner = OwnerName(node);
        bool inFuture = date > ctx.Today;
        bool looksLikeExpiry = ExpiryName.IsMatch(owner);

        // Data w przyszlosci to bomba zegarowa zawsze. Data w przeszlosci tylko wtedy,
        // gdy nazwa zdradza, ze byla granica waznosci - inaczej to zwykla stala historyczna.
        if (!inFuture && !looksLikeExpiry)
        {
            return null;
        }

        int line = SyntaxCache.LineOf(tree, node.SpanStart);

        if (IgnoreMarker.IsIgnored(SourceLine(tree, line)))
        {
            return null;
        }

        string snippet = Snippet(node);

        return ctx.Grader.WithDeadline(
            new Finding
            {
                Inspector = Id,
                Category = "Data w kodzie",
                Title = owner.Length > 0
                    ? $"{owner} = {date:yyyy-MM-dd}"
                    : $"Data {date:yyyy-MM-dd} wpisana na sztywno",
                Location = ctx.At(file, line),
                Detail = snippet,
                Severity = Severity.Info,
                Hint = inFuture
                    ? "Tego dnia kod zacznie zachowywać się inaczej. Ktoś o tym wie?"
                    : "Ta granica już minęła — sprawdź, czy kod nadal robi to, co powinien.",
            },
            date);
    }

    private static DateOnly? FromImplicitConstructor(ImplicitObjectCreationExpressionSyntax creation)
    {
        // "DateTime x = new(2026, 3, 1);" - typ bierzemy z deklaracji, bo w skladni go tu nie ma.
        var declaration = creation.FirstAncestorOrSelf<VariableDeclarationSyntax>();
        string? typeName = declaration?.Type.ToString();

        return typeName is null ? null : FromConstructor(typeName, creation.ArgumentList);
    }

    private static DateOnly? FromConstructor(string typeName, ArgumentListSyntax? arguments)
    {
        string simple = typeName.TrimEnd('?').Split('.').Last();
        if (!DateTypes.Contains(simple) || arguments is null || arguments.Arguments.Count < 3)
        {
            return null;
        }

        var numbers = new int[3];
        for (int i = 0; i < 3; i++)
        {
            if (arguments.Arguments[i].Expression is not LiteralExpressionSyntax literal
                || !literal.IsKind(SyntaxKind.NumericLiteralExpression)
                || literal.Token.Value is not int value)
            {
                return null;
            }

            numbers[i] = value;
        }

        return Build(numbers[0], numbers[1], numbers[2]);
    }

    private static DateOnly? FromParse(InvocationExpressionSyntax invocation)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax member)
        {
            return null;
        }

        string type = member.Expression.ToString().Split('.').Last();
        string method = member.Name.Identifier.Text;

        if (!DateTypes.Contains(type) || method is not ("Parse" or "ParseExact"))
        {
            return null;
        }

        if (invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression is not LiteralExpressionSyntax literal
            || !literal.IsKind(SyntaxKind.StringLiteralExpression))
        {
            return null;
        }

        var match = IsoLiteral.Match(literal.Token.ValueText);

        return match.Success
            ? Build(
                int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
                int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture),
                int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture))
            : null;
    }

    private static DateOnly? Build(int year, int month, int day)
    {
        if (year is < 1 or > 9999 || month is < 1 or > 12)
        {
            return null;
        }

        return day >= 1 && day <= DateTime.DaysInMonth(year, month)
            ? new DateOnly(year, month, day)
            : null;
    }

    /// <summary>Nazwa, pod ktora ta data zyje w kodzie - zmiennej, pola, wlasciwosci lub metody.</summary>
    private static string OwnerName(SyntaxNode node)
    {
        foreach (var ancestor in node.Ancestors())
        {
            switch (ancestor)
            {
                case VariableDeclaratorSyntax variable:
                    return variable.Identifier.Text;
                case PropertyDeclarationSyntax property:
                    return property.Identifier.Text;
                case AssignmentExpressionSyntax assignment:
                    return assignment.Left.ToString();
                case MethodDeclarationSyntax method:
                    return method.Identifier.Text;
                case BaseTypeDeclarationSyntax type:
                    return type.Identifier.Text;
            }
        }

        return string.Empty;
    }

    /// <summary>Cala linia zrodlowa - zeby zobaczyc komentarz wyciszajacy dopisany na jej koncu.</summary>
    private static string? SourceLine(SyntaxTree tree, int line)
    {
        var text = tree.GetText();

        return line >= 1 && line <= text.Lines.Count
            ? text.Lines[line - 1].ToString()
            : null;
    }

    private static string Snippet(SyntaxNode node)
    {
        string text = Regex.Replace(node.ToString(), @"\s+", " ").Trim();
        return text.Length <= 90 ? text : text[..87] + "…";
    }
}

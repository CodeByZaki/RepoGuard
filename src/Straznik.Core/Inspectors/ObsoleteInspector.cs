using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Straznik.Core.Configuration;
using Straznik.Core.Dates;
using Straznik.Core.Model;
using Straznik.Core.Scanning;
using Straznik.Core.Text;

namespace Straznik.Core.Inspectors;

/// <summary>
/// Pilnuje atrybutow [Obsolete]. Wycofanie czegos to obietnica, ze kiedys to zniknie -
/// a te obietnice starzeja sie w kodzie latami.
/// </summary>
public sealed class ObsoleteInspector : SyncInspector
{
    public override string Id => "obsolete";
    public override string DisplayName => "Wycofane API";

    public override bool IsEnabled(StraznikConfig config) => config.Obsolete.Enabled;

    /// <summary>Rozpoznaje "od 2024-01", "since 2024-01" - data pochodzenia, nie termin.</summary>
    private static readonly Regex SinceHint = new(
        @"\b(since|od|deprecated\s+since|wycofane\s+od)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    protected override IEnumerable<Finding> Inspect(ScanContext ctx, CancellationToken ct)
    {
        var declarations = CollectDeclarations(ctx, ct);
        if (declarations.Count == 0)
        {
            yield break;
        }

        var usages = CountUsages(ctx, declarations.Select(d => d.Name).ToHashSet(StringComparer.Ordinal), ct);

        foreach (var decl in declarations)
        {
            ct.ThrowIfCancellationRequested();
            yield return BuildFinding(ctx, decl, usages.GetValueOrDefault(decl.Name, 0));
        }
    }

    private Finding BuildFinding(ScanContext ctx, ObsoleteDeclaration decl, int usages)
    {
        string usageText = usages == 0
            ? "brak użyć w repozytorium — można usuwać"
            : $"{Plural.Usages(usages)} w repozytorium";

        var found = DateHunter.Find(decl.Message);
        var baseFinding = new Finding
        {
            Inspector = Id,
            Category = "Wycofane",

            // Nazwa rodzaju idzie po "[Obsolete]", zeby nie walczyc z rodzajem gramatycznym:
            // "klasa ... jest oznaczona", ale "interfejs ... jest oznaczony".
            Title = $"[Obsolete] {decl.Kind.ToLowerInvariant()} {decl.Name}",
            Location = decl.Location,
            Severity = Severity.Info,
        };

        if (found is null)
        {
            // Brak jakiejkolwiek daty. Nie zgadujemy terminu, ale jesli nikt juz tego nie uzywa,
            // to jest najtansza rzecz do posprzatania w calym repo.
            return baseFinding with
            {
                Detail = Join(decl.Message, usageText, "Wycofanie bez terminu."),
                Severity = usages == 0 ? Severity.Notice : Severity.Info,
                Hint = usages == 0
                    ? "Nic tego nie woła — usuń i zamknij temat."
                    : "Dopisz termin do komunikatu, np. [Obsolete(\"Usunięcie: 2026-06-30\")].",
            };
        }

        var date = found.Value.Date;
        bool isOrigin = SinceHint.IsMatch(decl.Message ?? string.Empty);

        if (!isOrigin)
        {
            // Data w komunikacie to zapowiedziany termin usuniecia.
            return ctx.Grader.WithDeadline(
                baseFinding with
                {
                    Detail = Join(decl.Message, usageText, $"Zapowiedziane usunięcie: {date:yyyy-MM-dd}."),
                    Hint = usages == 0
                        ? "Termin jest, użyć nie ma — usuń."
                        : $"Do zmigrowania przed terminem: {Plural.Usages(usages)}.",
                },
                date);
        }

        // Data to moment wycofania. Termin wyznacza polityka: ile miesiecy tolerujemy zaleganie.
        var deadline = date.AddMonths(ctx.Config.Obsolete.StaleAfterMonths);
        int months = MonthsBetween(date, ctx.Today);

        return ctx.Grader.WithDeadline(
            baseFinding with
            {
                Detail = Join(
                    decl.Message,
                    usageText,
                    $"Wycofane {date:yyyy-MM} — {Plural.Months(months)} temu."),
                Hint = $"Polityka repo: sprzątamy {Plural.Months(ctx.Config.Obsolete.StaleAfterMonths)} po wycofaniu.",
            },
            deadline);
    }

    private static string Join(params string?[] parts) =>
        string.Join(" · ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));

    private static int MonthsBetween(DateOnly from, DateOnly to) =>
        ((to.Year - from.Year) * 12) + to.Month - from.Month;

    private sealed record ObsoleteDeclaration(string Name, string Kind, string Location, string? Message);

    private static List<ObsoleteDeclaration> CollectDeclarations(ScanContext ctx, CancellationToken ct)
    {
        var result = new List<ObsoleteDeclaration>();

        foreach (string file in ctx.CSharpFiles)
        {
            ct.ThrowIfCancellationRequested();

            var tree = ctx.Syntax.Get(file);
            if (tree is null)
            {
                continue;
            }

            foreach (var attribute in tree.GetRoot(ct).DescendantNodes().OfType<AttributeSyntax>())
            {
                if (!IsObsolete(attribute))
                {
                    continue;
                }

                var owner = attribute.FirstAncestorOrSelf<MemberDeclarationSyntax>();
                if (owner is null)
                {
                    continue;
                }

                int line = SyntaxCache.LineOf(tree, attribute.SpanStart);

                result.Add(new ObsoleteDeclaration(
                    Name: NameOf(owner),
                    Kind: KindOf(owner),
                    Location: ctx.At(file, line),
                    Message: MessageOf(attribute)));
            }
        }

        return result;
    }

    private static bool IsObsolete(AttributeSyntax attribute)
    {
        // Obsluguje [Obsolete], [ObsoleteAttribute] i [System.Obsolete].
        string name = attribute.Name switch
        {
            QualifiedNameSyntax qualified => qualified.Right.Identifier.Text,
            SimpleNameSyntax simple => simple.Identifier.Text,
            _ => attribute.Name.ToString(),
        };

        return name is "Obsolete" or "ObsoleteAttribute";
    }

    private static string? MessageOf(AttributeSyntax attribute)
    {
        var argument = attribute.ArgumentList?.Arguments.FirstOrDefault();

        return argument?.Expression is LiteralExpressionSyntax literal
               && literal.IsKind(SyntaxKind.StringLiteralExpression)
            ? literal.Token.ValueText
            : null;
    }

    private static string NameOf(MemberDeclarationSyntax member) => member switch
    {
        BaseTypeDeclarationSyntax type => type.Identifier.Text,
        MethodDeclarationSyntax method => method.Identifier.Text,
        PropertyDeclarationSyntax property => property.Identifier.Text,
        EventDeclarationSyntax evt => evt.Identifier.Text,
        DelegateDeclarationSyntax del => del.Identifier.Text,
        ConstructorDeclarationSyntax ctor => ctor.Identifier.Text,
        FieldDeclarationSyntax field => field.Declaration.Variables.FirstOrDefault()?.Identifier.Text ?? "pole",
        EventFieldDeclarationSyntax evtField => evtField.Declaration.Variables.FirstOrDefault()?.Identifier.Text ?? "zdarzenie",
        _ => "składowa",
    };

    private static string KindOf(MemberDeclarationSyntax member) => member switch
    {
        ClassDeclarationSyntax => "Klasa",
        InterfaceDeclarationSyntax => "Interfejs",
        StructDeclarationSyntax => "Struktura",
        RecordDeclarationSyntax => "Rekord",
        EnumDeclarationSyntax => "Enum",
        MethodDeclarationSyntax => "Metoda",
        PropertyDeclarationSyntax => "Właściwość",
        FieldDeclarationSyntax => "Pole",
        DelegateDeclarationSyntax => "Delegat",
        ConstructorDeclarationSyntax => "Konstruktor",
        _ => "Składowa",
    };

    /// <summary>
    /// Liczy odwolania do nazwy w calym repo. Swiadomie robimy to na skladni, nie na modelu
    /// semantycznym - dzieki temu Straznik dziala na repozytorium, ktorego nie da sie zbudowac.
    /// </summary>
    private static Dictionary<string, int> CountUsages(ScanContext ctx, HashSet<string> names, CancellationToken ct)
    {
        var counts = names.ToDictionary(n => n, _ => 0, StringComparer.Ordinal);

        foreach (string file in ctx.CSharpFiles)
        {
            ct.ThrowIfCancellationRequested();

            var tree = ctx.Syntax.Get(file);
            if (tree is null)
            {
                continue;
            }

            foreach (var identifier in tree.GetRoot(ct).DescendantNodes().OfType<IdentifierNameSyntax>())
            {
                string text = identifier.Identifier.Text;
                if (counts.ContainsKey(text))
                {
                    counts[text]++;
                }
            }
        }

        return counts;
    }
}

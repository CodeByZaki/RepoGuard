using System.Text.Json;
using Straznik.Core.Model;
using Straznik.Core.Reporting;

namespace Straznik.Tests;

public class GuardTests
{
    [Fact]
    public void Czyste_repozytorium_nie_ma_znalezisk()
    {
        using var repo = new TempRepo().Write("A.cs", """
            class A
            {
                void M() { }
            }
            """);

        var result = repo.Scan();

        Assert.Empty(result.Findings);
        Assert.Null(result.Worst);
        Assert.Empty(result.Problems);
    }

    [Fact]
    public void Zlicza_to_co_przejrzal()
    {
        using var repo = new TempRepo()
            .Write("A.cs", "class A { }")
            .Write("B.cs", "class B { }")
            .Write("App.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>")
            .Write("appsettings.json", "{}");

        var stats = repo.Scan().Stats;

        Assert.Equal(2, stats.CSharpFiles);
        Assert.Equal(1, stats.ProjectFiles);
        Assert.Equal(1, stats.ConfigFiles);
    }

    [Fact]
    public void Respektuje_wykluczenia_z_konfiguracji()
    {
        using var repo = new TempRepo()
            .Write("src/A.cs", "// TODO(2025-01-01): coś")
            .Write("wygenerowane/B.cs", "// TODO(2025-01-01): coś innego");

        var result = repo.Scan(configure: c => c.Exclude.Add("wygenerowane/**"));

        Assert.Single(result.From("datedComments"));
    }

    [Fact]
    public void Domyslnie_nie_wychodzi_do_sieci()
    {
        // Inspektory sieciowe sa opcjonalne - Straznik ma dzialac tak samo bez internetu.
        using var repo = new TempRepo().Write("A.cs", "class A { }");

        var result = repo.Scan();

        Assert.DoesNotContain("Certyfikaty SSL", result.RanInspectors);
        Assert.DoesNotContain("Zależności NuGet", result.RanInspectors);
    }

    [Fact]
    public void Uszkodzony_json_nie_wywraca_obchodu()
    {
        using var repo = new TempRepo()
            .Write("appsettings.json", "{ to nie jest poprawny json")
            .Write("A.cs", "// TODO(2025-01-01): coś");

        var result = repo.Scan();

        Assert.Empty(result.Problems);
        Assert.Single(result.From("datedComments"));
    }

    [Fact]
    public void Sortuje_od_najpilniejszych()
    {
        using var repo = new TempRepo().Write("A.cs", """
            class A
            {
                // TODO(2027-01-01): daleko
                // FIXME(2024-01-01): dawno minęło
                // HACK(2026-09-20): za chwilę
                void M() { }
            }
            """);

        var kolejnosc = repo.Scan(today: "2026-09-13").Ordered.Select(f => f.Severity).ToList();

        Assert.Equal(Severity.Expired, kolejnosc[0]);
        Assert.Equal(Severity.Warning, kolejnosc[1]);
        Assert.Equal(Severity.Info, kolejnosc[2]);
    }
}

public class ReportingTests
{
    private static ScanResult Przykladowy()
    {
        using var repo = new TempRepo().Write("A.cs", """
            class A
            {
                // TODO(2025-03-01): usunąć obejście
                // HACK(2026-09-20): tymczasowy limit
                void M() { }
            }
            """);

        return repo.Scan(today: "2026-09-13");
    }

    [Fact]
    public void Raport_konsolowy_pokazuje_sedno()
    {
        string report = new ConsoleReporter(ConsoleStyle.Plain).Render(Przykladowy());

        Assert.Contains("S T R A Ż N I K", report);
        Assert.Contains("PO TERMINIE", report);
        Assert.Contains("usunąć obejście", report);
        Assert.Contains("-561 dni", report);
        Assert.Contains("Podsumowanie", report);
    }

    [Fact]
    public void Tryb_ascii_nie_zawiera_znakow_unicode_ani_kolorow()
    {
        string report = new ConsoleReporter(ConsoleStyle.Plain).Render(Przykladowy());

        Assert.DoesNotContain('\e', report);
        Assert.DoesNotContain('─', report);
        Assert.DoesNotContain('█', report);
    }

    [Fact]
    public void Raport_markdown_ma_tabele()
    {
        string report = MarkdownReporter.Render(Przykladowy());

        Assert.Contains("## 🛡️ Strażnik Daty Ważności", report);
        Assert.Contains("| Termin | Co | Gdzie |", report);
        Assert.Contains("PO TERMINIE", report);
    }

    [Fact]
    public void Raport_json_jest_poprawny_i_kompletny()
    {
        using var document = JsonDocument.Parse(JsonReporter.Render(Przykladowy()));
        var root = document.RootElement;

        Assert.Equal("2026-09-13", root.GetProperty("dataKontroli").GetString());
        Assert.Equal(1, root.GetProperty("podsumowanie").GetProperty("poTerminie").GetInt32());
        Assert.Equal(2, root.GetProperty("znaleziska").GetArrayLength());

        var pierwsze = root.GetProperty("znaleziska")[0];
        Assert.Equal("Expired", pierwsze.GetProperty("pilnosc").GetString());
        Assert.Equal(-561, pierwsze.GetProperty("dniDoTerminu").GetInt32());
    }

    [Fact]
    public void Raport_html_jest_samodzielny()
    {
        string report = HtmlReporter.Render(Przykladowy());

        Assert.StartsWith("<!doctype html>", report);
        Assert.Contains("<style>", report);
        Assert.DoesNotContain("<script", report);
        Assert.DoesNotContain("http://", report);
        Assert.Contains("usunąć obejście", report);
    }

    [Fact]
    public void Raport_html_ucieka_znaki_specjalne()
    {
        using var repo = new TempRepo().Write("A.cs", """
            class A
            {
                // TODO(2025-01-01): naprawić <script>alert(1)</script>
            }
            """);

        string report = HtmlReporter.Render(repo.Scan());

        Assert.DoesNotContain("<script>alert(1)</script>", report);
        Assert.Contains("&lt;script&gt;", report);
    }

    [Fact]
    public void Os_czasu_grupuje_terminy_po_miesiacach()
    {
        using var repo = new TempRepo().Write("A.cs", """
            class A
            {
                // TODO(2026-10-05): pierwsze
                // FIXME(2026-10-20): drugie
                // HACK(2026-12-01): trzecie
            }
            """);

        var buckets = Timeline.Build(repo.Scan(today: "2026-09-13"));

        Assert.Equal(12, buckets.Count);
        Assert.Equal(2, buckets.Single(b => b is { Year: 2026, Month: 10 }).Count);
        Assert.Equal(1, buckets.Single(b => b is { Year: 2026, Month: 12 }).Count);
        Assert.Equal(0, buckets.Single(b => b is { Year: 2026, Month: 11 }).Count);
    }
}

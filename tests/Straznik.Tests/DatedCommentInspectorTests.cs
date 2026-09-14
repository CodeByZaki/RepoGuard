using Straznik.Core.Model;

namespace Straznik.Tests;

public class DatedCommentInspectorTests
{
    [Fact]
    public void Znacznik_po_terminie_jest_przeterminowany()
    {
        using var repo = new TempRepo().Write("A.cs", """
            class A
            {
                // TODO(2025-03-01): usunąć obejście
                void M() { }
            }
            """);

        var finding = repo.Scan(today: "2026-09-13").Single("datedComments");

        Assert.Equal(Severity.Expired, finding.Severity);
        Assert.Equal(new DateOnly(2025, 3, 1), finding.Deadline);
        Assert.Equal(-561, finding.DaysLeft);
        Assert.Equal("TODO", finding.Category);
        Assert.Contains("A.cs:3", finding.Location);
    }

    [Fact]
    public void Data_moze_byc_w_sasiedniej_linii_komentarza()
    {
        // Ludzie pisza termin w kolejnej linii - Straznik sklada sasiadujace
        // komentarze jednoliniowe w jeden blok, zanim zacznie szukac daty.
        using var repo = new TempRepo().Write("A.cs", """
            class A
            {
                // TODO: wyrzucić stary parser
                // najpóźniej 2026-10-01
                void M() { }
            }
            """);

        var finding = repo.Scan(today: "2026-09-13").Single("datedComments");

        Assert.Equal(new DateOnly(2026, 10, 1), finding.Deadline);
        Assert.Equal(Severity.Warning, finding.Severity);
    }

    [Fact]
    public void Znacznik_w_literale_tekstowym_nie_jest_znaleziskiem()
    {
        // To jest powod, dla ktorego Straznik uzywa Roslyna, a nie wyrazen regularnych:
        // "TODO" w stringu to dane, nie obietnica programisty.
        using var repo = new TempRepo().Write("A.cs", """
            class A
            {
                string s = "TODO(2020-01-01): to tylko tekst";
            }
            """);

        Assert.Empty(repo.Scan().From("datedComments"));
    }

    [Fact]
    public void Znaczniki_bez_daty_sa_liczone_ale_nie_zglaszane()
    {
        using var repo = new TempRepo().Write("A.cs", """
            class A
            {
                // TODO: kiedyś to poprawić
                // FIXME: brzydkie
                // HACK(2026-10-01): a to ma termin
                void M() { }
            }
            """);

        var result = repo.Scan(today: "2026-09-13");

        Assert.Single(result.From("datedComments"));
        Assert.Equal(2, result.Stats.UndatedMarkers);
    }

    [Fact]
    public void Data_nie_przecieka_miedzy_znacznikami_w_jednym_bloku()
    {
        // Trzy znaczniki obok siebie, jedna data. Nie da sie zgadnac, do ktorego nalezy,
        // wiec termin dostaje wylacznie ten, ktory ma date we wlasnej linii.
        using var repo = new TempRepo().Write("A.cs", """
            class A
            {
                // TODO: kiedyś to poprawić
                // FIXME: brzydkie
                // HACK(2026-10-01): a to ma termin
                void M() { }
            }
            """);

        var finding = repo.Scan(today: "2026-09-13").Single("datedComments");

        Assert.Equal("HACK", finding.Category);
        Assert.Equal(new DateOnly(2026, 10, 1), finding.Deadline);
    }

    [Fact]
    public void Linia_z_wyciszeniem_jest_pomijana()
    {
        using var repo = new TempRepo().Write("A.cs", """
            class A
            {
                // TODO(2025-03-01): przykład w dokumentacji, nie zobowiązanie (straznik:ignore)
                // TODO(2025-03-01): a to jest prawdziwy dług
                void M() { }
            }
            """);

        var result = repo.Scan(today: "2026-09-13");

        var finding = result.Single("datedComments");
        Assert.Contains("prawdziwy dług", finding.Title);
        Assert.Equal(0, result.Stats.UndatedMarkers);
    }

    [Fact]
    public void Znaczniki_bez_daty_mozna_wlaczyc_w_konfiguracji()
    {
        using var repo = new TempRepo().Write("A.cs", """
            class A
            {
                // TODO: kiedyś to poprawić
            }
            """);

        var result = repo.Scan(configure: c => c.DatedComments.ReportUndated = true);
        var finding = result.Single("datedComments");

        Assert.Equal(Severity.Info, finding.Severity);
        Assert.Null(finding.Deadline);
    }

    [Fact]
    public void Komentarz_blokowy_tez_jest_czytany()
    {
        using var repo = new TempRepo().Write("A.cs", """
            class A
            {
                /*
                 * TEMP: tymczasowa ścieżka zapisu
                 * do usunięcia w Q1 2027
                 */
                void M() { }
            }
            """);

        var finding = repo.Scan(today: "2026-09-13").Single("datedComments");

        Assert.Equal(new DateOnly(2027, 3, 31), finding.Deadline);
    }
}

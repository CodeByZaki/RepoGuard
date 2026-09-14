using Straznik.Core.Model;

namespace Straznik.Tests;

public class ObsoleteInspectorTests
{
    [Fact]
    public void Zapowiedziany_termin_usuniecia_jest_pilnowany()
    {
        using var repo = new TempRepo().Write("A.cs", """
            using System;

            [Obsolete("Używaj B. Planowane usunięcie: 2026-09-30.")]
            class A { }
            """);

        var finding = repo.Scan(today: "2026-09-13").Single("obsolete");

        Assert.Equal(new DateOnly(2026, 9, 30), finding.Deadline);
        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.Contains("klasa A", finding.Title);
    }

    [Fact]
    public void Data_wycofania_wyznacza_termin_przez_polityke_repo()
    {
        // "od 2024-03" to moment wycofania, nie termin. Termin bierze sie z polityki:
        // domyslnie 12 miesiecy na posprzatanie.
        using var repo = new TempRepo().Write("A.cs", """
            using System;

            [Obsolete("Wycofane od 2024-03. Zastąpione przez B.")]
            class A { }
            """);

        var finding = repo.Scan(today: "2026-09-13").Single("obsolete");

        Assert.Equal(new DateOnly(2025, 3, 31), finding.Deadline);
        Assert.Equal(Severity.Expired, finding.Severity);
    }

    [Fact]
    public void Polityke_sprzatania_da_sie_zmienic()
    {
        using var repo = new TempRepo().Write("A.cs", """
            using System;

            [Obsolete("Wycofane od 2024-03.")]
            class A { }
            """);

        var finding = repo
            .Scan(today: "2026-09-13", configure: c => c.Obsolete.StaleAfterMonths = 36)
            .Single("obsolete");

        Assert.Equal(new DateOnly(2027, 3, 31), finding.Deadline);
    }

    [Fact]
    public void Brak_uzyc_to_gotowa_do_sprzatniecia_zaleglosc()
    {
        using var repo = new TempRepo().Write("A.cs", """
            using System;

            class Kontener
            {
                [Obsolete("Nieużywane.")]
                public void StaraMetoda() { }
            }
            """);

        var finding = repo.Scan().Single("obsolete");

        Assert.Equal(Severity.Notice, finding.Severity);
        Assert.Contains("brak użyć", finding.Detail);
        Assert.Null(finding.Deadline);
    }

    [Fact]
    public void Uzycia_sa_liczone_w_calym_repozytorium()
    {
        using var repo = new TempRepo()
            .Write("A.cs", """
                using System;

                [Obsolete("Używaj B. Usunięcie: 2026-12-01.")]
                class StaryKlient { }
                """)
            .Write("B.cs", """
                class Uzytkownik
                {
                    private StaryKlient _a = new StaryKlient();
                }
                """);

        var finding = repo.Scan(today: "2026-09-13").Single("obsolete");

        Assert.Contains("użycia", finding.Detail);
        Assert.DoesNotContain("brak użyć", finding.Detail);
    }
}

public class HardcodedDateInspectorTests
{
    [Fact]
    public void Data_w_przyszlosci_to_bomba_zegarowa()
    {
        using var repo = new TempRepo().Write("A.cs", """
            using System;

            class A
            {
                static readonly DateTime Przelaczenie = new DateTime(2026, 11, 1);
            }
            """);

        var finding = repo.Scan(today: "2026-09-13").Single("hardcodedDates");

        Assert.Equal(new DateOnly(2026, 11, 1), finding.Deadline);
        Assert.Contains("Przelaczenie", finding.Title);
    }

    [Fact]
    public void Minieta_granica_waznosci_jest_zglaszana()
    {
        using var repo = new TempRepo().Write("A.cs", """
            using System;

            class A
            {
                static readonly DateTime PromocjaKoniec = new DateTime(2025, 12, 24);
            }
            """);

        var finding = repo.Scan(today: "2026-09-13").Single("hardcodedDates");

        Assert.Equal(Severity.Expired, finding.Severity);
    }

    [Fact]
    public void Zwykla_data_historyczna_nie_jest_zglaszana()
    {
        // Nazwa nie sugeruje terminu, a data jest w przeszlosci - to stala, nie zobowiazanie.
        using var repo = new TempRepo().Write("A.cs", """
            using System;

            class A
            {
                static readonly DateTime DataZalozeniaFirmy = new DateTime(2019, 4, 2);
            }
            """);

        Assert.Empty(repo.Scan(today: "2026-09-13").From("hardcodedDates"));
    }

    [Fact]
    public void Wyciszenie_dziala_takze_na_daty_w_kodzie()
    {
        using var repo = new TempRepo().Write("A.cs", """
            using System;

            class A
            {
                static readonly DateTime Ustawowy = new DateTime(2026, 11, 1); // straznik:ignore
                static readonly DateTime Nasz = new DateTime(2026, 11, 2);
            }
            """);

        var finding = repo.Scan(today: "2026-09-13").Single("hardcodedDates");

        Assert.Equal(new DateOnly(2026, 11, 2), finding.Deadline);
    }

    [Fact]
    public void Daty_w_inicjalizatorze_kolekcji_to_dane_nie_terminy()
    {
        // Tablica dat konca wsparcia to dane referencyjne - kod nie zmieni sie tego dnia.
        using var repo = new TempRepo().Write("A.cs", """
            using System;
            using System.Collections.Generic;

            class A
            {
                static readonly Dictionary<string, DateOnly> Wsparcie = new()
                {
                    ["net8.0"] = new DateOnly(2026, 11, 10),
                    ["net9.0"] = new DateOnly(2026, 5, 12),
                };
            }
            """);

        Assert.Empty(repo.Scan(today: "2026-09-13").From("hardcodedDates"));
    }

    [Fact]
    public void Wartosci_wartownicze_sa_pomijane()
    {
        using var repo = new TempRepo().Write("A.cs", """
            using System;

            class A
            {
                static readonly DateTime Nigdy = new DateTime(9999, 12, 31);
                static readonly DateTime Zawsze = new DateTime(1900, 1, 1);
            }
            """);

        Assert.Empty(repo.Scan(today: "2026-09-13").From("hardcodedDates"));
    }

    [Fact]
    public void Rozpoznaje_skrocony_konstruktor_i_Parse()
    {
        using var repo = new TempRepo().Write("A.cs", """
            using System;

            class A
            {
                static readonly DateOnly KoniecWsparcia = new(2026, 9, 20);
                static readonly DateTime TerminMigracji = DateTime.Parse("2026-09-25");
            }
            """);

        var findings = repo.Scan(today: "2026-09-13").From("hardcodedDates");

        Assert.Equal(2, findings.Count);
        Assert.All(findings, f => Assert.Equal(Severity.Warning, f.Severity));
        Assert.Contains(findings, f => f.Deadline == new DateOnly(2026, 9, 20));
        Assert.Contains(findings, f => f.Deadline == new DateOnly(2026, 9, 25));
    }
}

public class TargetFrameworkInspectorTests
{
    [Fact]
    public void Zna_daty_konca_wsparcia_platformy()
    {
        using var repo = new TempRepo().Write("App.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);

        var finding = repo.Scan(today: "2026-09-13").Single("targetFramework");

        Assert.Equal(new DateOnly(2026, 11, 10), finding.Deadline);
        Assert.Equal(Severity.Notice, finding.Severity);
    }

    [Fact]
    public void Obsluguje_wiele_platform_i_sufiksy_systemowe()
    {
        using var repo = new TempRepo().Write("App.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFrameworks>net6.0;net8.0-windows10.0.19041.0</TargetFrameworks>
              </PropertyGroup>
            </Project>
            """);

        var findings = repo.Scan(today: "2026-09-13").From("targetFramework");

        Assert.Equal(2, findings.Count);
        Assert.Contains(findings, f => f.Deadline == new DateOnly(2024, 11, 12));
        Assert.Contains(findings, f => f.Deadline == new DateOnly(2026, 11, 10));
    }

    [Fact]
    public void Czyta_centralny_TargetFramework_z_Directory_Build_props()
    {
        using var repo = new TempRepo().Write("Directory.Build.props", """
            <Project>
              <PropertyGroup>
                <TargetFramework>net9.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);

        var finding = repo.Scan(today: "2026-09-13").Single("targetFramework");

        Assert.Equal(new DateOnly(2026, 5, 12), finding.Deadline);
        Assert.Equal(Severity.Expired, finding.Severity);
    }

    [Fact]
    public void Czyta_takze_przypiety_sdk_z_global_json()
    {
        using var repo = new TempRepo().Write("global.json", """
            { "sdk": { "version": "6.0.428" } }
            """);

        var finding = repo.Scan(today: "2026-09-13").Single("targetFramework");

        Assert.Equal("SDK", finding.Category);
        Assert.Equal(Severity.Expired, finding.Severity);
    }
}

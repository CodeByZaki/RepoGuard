using Straznik.Core.Dates;

namespace Straznik.Tests;

public class DateHunterTests
{
    [Theory]
    [InlineData("TODO(2026-03-01): zrobić", "2026-03-01")]
    [InlineData("TODO 2026/03/01", "2026-03-01")]
    [InlineData("usunąć do 01.03.2026", "2026-03-01")]
    [InlineData("usunąć do 01/03/2026", "2026-03-01")]
    public void Znajduje_daty_dzienne(string text, string expected)
    {
        var found = DateHunter.Find(text);

        Assert.NotNull(found);
        Assert.Equal(DateOnly.ParseExact(expected, "yyyy-MM-dd"), found!.Value.Date);
        Assert.Equal(DatePrecision.Day, found.Value.Precision);
    }

    [Theory]
    [InlineData("TODO(2026-02): posprzątać", "2026-02-28")]
    [InlineData("HACK do końca marca 2026", "2026-03-31")]
    [InlineData("remove by March 2026", "2026-03-31")]
    [InlineData("wygasa w lutym 2028", "2028-02-29")]
    public void Termin_miesieczny_liczy_sie_na_koniec_miesiaca(string text, string expected)
    {
        var found = DateHunter.Find(text);

        Assert.NotNull(found);
        Assert.Equal(DateOnly.ParseExact(expected, "yyyy-MM-dd"), found!.Value.Date);
        Assert.Equal(DatePrecision.Month, found.Value.Precision);
    }

    [Theory]
    [InlineData("dowieziemy w Q2 2026", "2026-06-30")]
    [InlineData("planowane na 2026 Q4", "2026-12-31")]
    public void Termin_kwartalny_liczy_sie_na_koniec_kwartalu(string text, string expected)
    {
        var found = DateHunter.Find(text);

        Assert.NotNull(found);
        Assert.Equal(DateOnly.ParseExact(expected, "yyyy-MM-dd"), found!.Value.Date);
        Assert.Equal(DatePrecision.Quarter, found.Value.Precision);
    }

    [Theory]
    [InlineData("zwykły komentarz bez terminu")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("wersja 1.2.3 i numer 12345")]
    [InlineData("rok 1899 to za daleko wstecz")]
    [InlineData("2026-13-01 nie istnieje")]
    [InlineData("2026-02-30 nie istnieje")]
    public void Nie_wymysla_dat(string? text) => Assert.Null(DateHunter.Find(text));

    [Fact]
    public void Data_dzienna_wygrywa_z_sama_nazwa_miesiaca()
    {
        // "marzec 2026" i "2026-03-05" w jednym zdaniu - precyzyjniejszy zapis ma pierwszenstwo.
        var found = DateHunter.Find("marzec 2026, a dokładnie 2026-03-05");

        Assert.NotNull(found);
        Assert.Equal(new DateOnly(2026, 3, 5), found!.Value.Date);
    }
}

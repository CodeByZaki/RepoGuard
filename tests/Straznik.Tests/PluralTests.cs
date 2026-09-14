using Straznik.Core.Text;

namespace Straznik.Tests;

public class PluralTests
{
    [Theory]
    [InlineData(0, "0 plików")]
    [InlineData(1, "1 plik")]
    [InlineData(2, "2 pliki")]
    [InlineData(4, "4 pliki")]
    [InlineData(5, "5 plików")]
    [InlineData(11, "11 plików")]
    [InlineData(12, "12 plików")]
    [InlineData(14, "14 plików")]
    [InlineData(22, "22 pliki")]
    [InlineData(25, "25 plików")]
    [InlineData(94, "94 pliki")]
    [InlineData(112, "112 plików")]
    [InlineData(122, "122 pliki")]
    public void Odmienia_liczebniki_po_polsku(int count, string expected) =>
        Assert.Equal(expected, Plural.Files(count));

    [Theory]
    [InlineData(1, "1 użycie")]
    [InlineData(3, "3 użycia")]
    [InlineData(7, "7 użyć")]
    public void Odmienia_uzycia(int count, string expected) =>
        Assert.Equal(expected, Plural.Usages(count));
}

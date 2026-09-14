using System.Text.Json;
using System.Text.Json.Serialization;
using Straznik.Core.Model;

namespace Straznik.Core.Reporting;

/// <summary>Wynik w formie maszynowej - do porownan miedzy przebiegami i do wlasnych integracji.</summary>
public static class JsonReporter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Render(ScanResult result) => JsonSerializer.Serialize(
        new
        {
            repozytorium = result.RootPath,
            dataKontroli = result.Today.ToString("yyyy-MM-dd"),
            podsumowanie = new
            {
                poTerminie = result.Count(Severity.Expired),
                wygasaWkrotce = result.Count(Severity.Warning),
                zblizaSie = result.Count(Severity.Notice),
                doWiadomosci = result.Count(Severity.Info),
                znacznikiBezDaty = result.Stats.UndatedMarkers,
            },
            inspektorzy = result.RanInspectors,
            problemy = result.Problems.Select(p => new { inspektor = p.Inspector, komunikat = p.Message }),
            znaleziska = result.Ordered.Select(f => new
            {
                inspektor = f.Inspector,
                kategoria = f.Category,
                opis = f.Title,
                lokalizacja = f.Location,
                szczegoly = f.Detail,
                termin = f.Deadline?.ToString("yyyy-MM-dd"),
                dniDoTerminu = f.DaysLeft,
                pilnosc = f.Severity.ToString(),
                podpowiedz = f.Hint,
            }),
        },
        Options);
}

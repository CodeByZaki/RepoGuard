namespace Straznik.Core.Model;

/// <summary>
/// Jak pilna jest sprawa. Kolejnosc ma znaczenie - sortujemy i porownujemy po wartosci.
/// </summary>
public enum Severity
{
    /// <summary>Termin jest tak daleko, ze to tylko notatka.</summary>
    Info = 0,

    /// <summary>Termin jeszcze odlegly, ale juz warto go wziac do planu.</summary>
    Notice = 1,

    /// <summary>Termin tuz-tuz. To jest moment, w ktorym Straznik zaczyna krzyczec.</summary>
    Warning = 2,

    /// <summary>Po terminie. Dlug, ktory juz zapadl.</summary>
    Expired = 3,
}

public static class SeverityExtensions
{
    public static string Label(this Severity severity) => severity switch
    {
        Severity.Expired => "PO TERMINIE",
        Severity.Warning => "WYGASA WKRÓTCE",
        Severity.Notice => "ZBLIŻA SIĘ",
        _ => "DO WIADOMOŚCI",
    };
}

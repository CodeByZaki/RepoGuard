using Straznik.Core.Model;

namespace Straznik.Core.Dates;

/// <summary>
/// Zamienia date terminu na pilnosc. Jedno miejsce, zeby wszystkie inspektory
/// oceenialy czas tak samo.
/// </summary>
public sealed class Grader(DateOnly today, int warnWithinDays, int noticeWithinDays)
{
    public DateOnly Today { get; } = today;

    public int DaysLeft(DateOnly deadline) => deadline.DayNumber - Today.DayNumber;

    public Severity Grade(DateOnly deadline)
    {
        int days = DaysLeft(deadline);

        if (days < 0)
        {
            return Severity.Expired;
        }

        if (days <= warnWithinDays)
        {
            return Severity.Warning;
        }

        return days <= noticeWithinDays ? Severity.Notice : Severity.Info;
    }

    /// <summary>Uzupelnia znalezisko o termin, liczbe dni i pilnosc.</summary>
    public Finding WithDeadline(Finding finding, DateOnly deadline) => finding with
    {
        Deadline = deadline,
        DaysLeft = DaysLeft(deadline),
        Severity = Grade(deadline),
    };
}

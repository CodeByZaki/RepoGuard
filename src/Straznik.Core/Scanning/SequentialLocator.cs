namespace Straznik.Core.Scanning;

/// <summary>
/// Przyblizone numery linii dla kluczy JSON.
/// System.Text.Json nie oddaje pozycji z JsonDocument, ale dokument obchodzimy w kolejnosci
/// zapisu - wiec wystarczy przesuwac sie po surowym tekscie tym samym tempem.
/// </summary>
public sealed class SequentialLocator(string text)
{
    private int _offset;

    /// <summary>Numer linii (od 1) najblizszego wystapienia klucza. 1, gdy nie znaleziono.</summary>
    public int LineOf(string key)
    {
        int index = text.IndexOf($"\"{key}\"", _offset, StringComparison.Ordinal);

        if (index < 0)
        {
            // Klucz moze byc wczesniej niz biezacy offset (np. po cofnieciu sie w strukturze).
            index = text.IndexOf($"\"{key}\"", StringComparison.Ordinal);
            if (index < 0)
            {
                return 1;
            }
        }
        else
        {
            _offset = index + key.Length;
        }

        int line = 1;
        for (int i = 0; i < index; i++)
        {
            if (text[i] == '\n')
            {
                line++;
            }
        }

        return line;
    }
}

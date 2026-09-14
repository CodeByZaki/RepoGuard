using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace Straznik.Core.Scanning;

/// <summary>
/// Parsuje pliki .cs raz i oddaje drzewa skladni kolejnym inspektorom.
/// Trzy inspektory czytaja ten sam kod - nie ma powodu, zeby parsowac go trzy razy.
/// </summary>
public sealed class SyntaxCache
{
    private readonly Dictionary<string, SyntaxTree?> _trees = new(StringComparer.OrdinalIgnoreCase);

    public SyntaxTree? Get(string path)
    {
        if (_trees.TryGetValue(path, out var cached))
        {
            return cached;
        }

        SyntaxTree? tree = null;
        try
        {
            var text = SourceText.From(File.ReadAllText(path));
            tree = CSharpSyntaxTree.ParseText(text, path: path);
        }
        catch (IOException)
        {
            // Plik zniknal albo jest zablokowany - Straznik nie przerywa przez to calego obchodu.
        }
        catch (UnauthorizedAccessException)
        {
        }

        _trees[path] = tree;
        return tree;
    }

    /// <summary>Numer linii (liczony od 1) dla pozycji w pliku.</summary>
    public static int LineOf(SyntaxTree tree, int position) =>
        tree.GetLineSpan(new TextSpan(position, 0)).StartLinePosition.Line + 1;
}

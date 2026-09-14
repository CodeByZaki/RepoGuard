using Microsoft.Extensions.FileSystemGlobbing;

namespace Straznik.Core.Scanning;

/// <summary>Wyszukiwanie plikow po wzorcach glob, z jedna wspolna lista wykluczen.</summary>
public sealed class FileScanner(string rootPath, IReadOnlyList<string> exclude)
{
    private readonly string _root = Path.GetFullPath(rootPath);

    public IReadOnlyList<string> Find(params string[] include) => Find((IEnumerable<string>)include);

    public IReadOnlyList<string> Find(IEnumerable<string> include)
    {
        var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);
        matcher.AddIncludePatterns(include);
        matcher.AddExcludePatterns(exclude);

        return matcher.GetResultsInFullPath(_root)
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}

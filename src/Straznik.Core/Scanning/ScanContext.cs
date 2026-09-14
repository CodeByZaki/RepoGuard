using Straznik.Core.Configuration;
using Straznik.Core.Dates;

namespace Straznik.Core.Scanning;

/// <summary>Wszystko, czego inspektor potrzebuje, zeby wykonac swoja robote.</summary>
public sealed class ScanContext
{
    public required string RootPath { get; init; }
    public required StraznikConfig Config { get; init; }
    public required Grader Grader { get; init; }
    public required FileScanner Scanner { get; init; }
    public required SyntaxCache Syntax { get; init; }

    public required IReadOnlyList<string> CSharpFiles { get; init; }
    public required IReadOnlyList<string> ProjectFiles { get; init; }

    public DateOnly Today => Grader.Today;

    /// <summary>Sciezka wzgledem korzenia repo, zawsze z ukosnikami w przod - zeby raporty byly takie same na kazdym systemie.</summary>
    public string Relative(string path) =>
        Path.GetRelativePath(RootPath, path).Replace('\\', '/');

    public string At(string path, int line) => $"{Relative(path)}:{line}";
}

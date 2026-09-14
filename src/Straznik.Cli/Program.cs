using System.Text;
using Straznik.Cli;
using Straznik.Core.Configuration;
using Straznik.Core.Model;
using Straznik.Core.Notifications;
using Straznik.Core.Reporting;
using Straznik.Core.Scanning;

Console.OutputEncoding = Encoding.UTF8;

CommandLineOptions options;
try
{
    options = CommandLineOptions.Parse(args);
}
catch (Exception e) when (e is ArgumentException or FormatException)
{
    Console.Error.WriteLine($"Błąd: {e.Message}");
    Console.Error.WriteLine();
    Console.Error.WriteLine(CommandLineOptions.HelpText);
    return 64;
}

if (options.Help)
{
    Console.WriteLine(CommandLineOptions.HelpText);
    return 0;
}

if (options.Command == "init")
{
    string target = options.OutputPath ?? Path.Combine(options.Path, "straznik.json");

    if (File.Exists(target))
    {
        Console.Error.WriteLine($"Plik {target} już istnieje — nie nadpisuję.");
        return 1;
    }

    await File.WriteAllTextAsync(target, StraznikConfig.Serialize(new StraznikConfig()));
    Console.WriteLine($"Utworzono {target}. Zajrzyj tam i dostosuj progi do swojego repozytorium.");
    return 0;
}

if (options.Command != "scan")
{
    Console.Error.WriteLine($"Nieznane polecenie: {options.Command}");
    Console.Error.WriteLine();
    Console.Error.WriteLine(CommandLineOptions.HelpText);
    return 64;
}

if (!Directory.Exists(options.Path))
{
    Console.Error.WriteLine($"Katalog nie istnieje: {options.Path}");
    return 66;
}

string configPath = options.ConfigPath ?? Path.Combine(options.Path, "straznik.json");
StraznikConfig config;

try
{
    config = StraznikConfig.Load(configPath);
}
catch (Exception e) when (e is System.Text.Json.JsonException or InvalidOperationException)
{
    Console.Error.WriteLine($"Nie udało się wczytać konfiguracji {configPath}: {e.Message}");
    return 78;
}

var today = options.Today ?? DateOnly.FromDateTime(DateTime.Now);

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cancellation.Cancel();
};

ScanResult result;
try
{
    result = await new Guard().RunAsync(options.Path, config, today, cancellation.Token);
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("Przerwano.");
    return 130;
}

string report = options.Format switch
{
    OutputFormat.Markdown => MarkdownReporter.Render(result),
    OutputFormat.Html => HtmlReporter.Render(result),
    OutputFormat.Json => JsonReporter.Render(result),
    _ => new ConsoleReporter(new ConsoleStyle(
        Color: !options.NoColor && !options.Ascii,
        Unicode: !options.Ascii)).Render(result),
};

if (options.OutputPath is { Length: > 0 } outputPath)
{
    string? directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
    if (directory is { Length: > 0 })
    {
        Directory.CreateDirectory(directory);
    }

    await File.WriteAllTextAsync(outputPath, report, new UTF8Encoding(false), cancellation.Token);

    if (!options.Quiet)
    {
        Console.WriteLine($"Raport zapisany: {outputPath}");
    }
}
else if (!options.Quiet)
{
    Console.Write(report);
}

if (options.WebhookUrl is { Length: > 0 } webhook)
{
    bool sent = await new WebhookNotifier().SendAsync(webhook, result, cancellation.Token);

    if (!options.Quiet)
    {
        Console.WriteLine(sent
            ? "Powiadomienie wysłane na webhook."
            : "Nie udało się wysłać powiadomienia na webhook.");
    }
}

return options.FailOn switch
{
    FailOn.None => 0,
    FailOn.Expiring when result.Count(Severity.Expired) > 0 => 2,
    FailOn.Expiring when result.Count(Severity.Warning) > 0 => 1,
    FailOn.Expired when result.Count(Severity.Expired) > 0 => 2,
    _ => 0,
};

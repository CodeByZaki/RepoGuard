namespace Straznik.Cli;

public enum OutputFormat
{
    Console,
    Markdown,
    Html,
    Json,
}

public enum FailOn
{
    None,
    Expiring,
    Expired,
}

public sealed class CommandLineOptions
{
    public string Command { get; private init; } = "scan";
    public string Path { get; private init; } = ".";
    public string? ConfigPath { get; private init; }
    public DateOnly? Today { get; private init; }
    public OutputFormat Format { get; private init; } = OutputFormat.Console;
    public string? OutputPath { get; private init; }
    public FailOn FailOn { get; private init; } = FailOn.Expired;
    public bool Ascii { get; private init; }
    public bool NoColor { get; private init; }
    public bool ForceColor { get; private init; }
    public string? WebhookUrl { get; private init; }
    public bool Quiet { get; private init; }
    public bool Help { get; private init; }

    public static CommandLineOptions Parse(string[] args)
    {
        string command = args.Length > 0 && !args[0].StartsWith('-') ? args[0] : "scan";
        int start = command == "scan" && (args.Length == 0 || args[0].StartsWith('-')) ? 0 : 1;

        string path = ".";
        string? configPath = null;
        DateOnly? today = null;
        var format = OutputFormat.Console;
        string? output = null;
        var failOn = FailOn.Expired;
        bool ascii = false;
        bool noColor = false;
        bool color = false;
        string? webhook = Environment.GetEnvironmentVariable("STRAZNIK_WEBHOOK_URL");
        bool quiet = false;
        bool help = false;

        for (int i = start; i < args.Length; i++)
        {
            string arg = args[i];

            switch (arg)
            {
                case "--path" or "-p":
                    path = Next(args, ref i, arg);
                    break;
                case "--config" or "-c":
                    configPath = Next(args, ref i, arg);
                    break;
                case "--today":
                    today = DateOnly.ParseExact(Next(args, ref i, arg), "yyyy-MM-dd");
                    break;
                case "--format" or "-f":
                    format = ParseEnum<OutputFormat>(Next(args, ref i, arg), arg);
                    break;
                case "--out" or "-o":
                    output = Next(args, ref i, arg);
                    break;
                case "--fail-on":
                    failOn = ParseEnum<FailOn>(Next(args, ref i, arg), arg);
                    break;
                case "--webhook":
                    webhook = Next(args, ref i, arg);
                    break;
                case "--ascii":
                    ascii = true;
                    break;
                case "--no-color":
                    noColor = true;
                    break;
                case "--color":
                    color = true;
                    break;
                case "--quiet" or "-q":
                    quiet = true;
                    break;
                case "--help" or "-h":
                    help = true;
                    break;
                default:
                    throw new ArgumentException($"Nieznana opcja: {arg}");
            }
        }

     
        // Kolor znika domyslnie, gdy wyjscie idzie do pliku lub potoku - inaczej raport
        // w logach CI jest usiany sekwencjami sterujacymi. Opcja --color (albo zmienna
        // FORCE_COLOR) przywraca go swiadomie: przydaje sie przy `| less -R` i przy
        // generowaniu obrazow raportu do dokumentacji.
        bool forceColor = color || Environment.GetEnvironmentVariable("FORCE_COLOR") is not null;

        if (!forceColor && (Console.IsOutputRedirected || Environment.GetEnvironmentVariable("NO_COLOR") is not null))
        {
            noColor = true;
        }

        return new CommandLineOptions
        {
            Command = command,
            Path = path,
            ConfigPath = configPath,
            Today = today,
            Format = format,
            OutputPath = output,
            FailOn = failOn,
            Ascii = ascii,
            NoColor = noColor,
            ForceColor = forceColor,
            WebhookUrl = webhook,
            Quiet = quiet,
            Help = help,
        };
    }

    private static string Next(string[] args, ref int index, string option)
    {
        if (index + 1 >= args.Length)
        {
            throw new ArgumentException($"Opcja {option} wymaga wartości.");
        }

        return args[++index];
    }

    private static T ParseEnum<T>(string value, string option) where T : struct, Enum =>
        Enum.TryParse<T>(value, ignoreCase: true, out var parsed)
            ? parsed
            : throw new ArgumentException(
                $"Nieprawidłowa wartość opcji {option}: '{value}'. Dozwolone: {string.Join(", ", Enum.GetNames<T>()).ToLowerInvariant()}.");

    public const string HelpText = """
        Strażnik Daty Ważności — pilnuje wszystkiego w repozytorium, co ma termin.

        UŻYCIE
          straznik [scan] [opcje]
          straznik init [--out straznik.json]

        OPCJE
          -p, --path <katalog>     Katalog repozytorium do sprawdzenia (domyślnie: .)
          -c, --config <plik>      Plik konfiguracji (domyślnie: straznik.json w katalogu repo)
              --today <RRRR-MM-DD> Udawana data kontroli — do testów i demonstracji
          -f, --format <format>    console | markdown | html | json (domyślnie: console)
          -o, --out <plik>         Zapisz raport do pliku zamiast na ekran
              --fail-on <poziom>   none | expiring | expired (domyślnie: expired)
              --webhook <url>      Wyślij podsumowanie na webhook (Discord, Slack lub własny)
                                   Można też ustawić zmienną STRAZNIK_WEBHOOK_URL
              --ascii              Bez znaków Unicode (stare konsole)
              --no-color           Bez kolorów
              --color              Wymuś kolory mimo przekierowania wyjścia
                                   (np. przy `| less -R`; też przez FORCE_COLOR)
          -q, --quiet              Tylko kod wyjścia, bez raportu na ekranie
          -h, --help               Ta pomoc

        KODY WYJŚCIA
          0   nic nie wygasa (albo --fail-on none)
          1   coś wygasa wkrótce (przy --fail-on expiring)
          2   coś jest po terminie

        PRZYKŁADY
          straznik
          straznik --path ../moj-projekt --format html --out raport.html
          straznik --fail-on expiring --webhook https://discord.com/api/webhooks/...
        """;
}

"""Renderuje wyjście Strażnika (z sekwencjami ANSI) do samodzielnego pliku SVG.

Obrazek w README nie jest zrzutem ekranu ani makietą — powstaje z prawdziwego
uruchomienia programu. Ten skrypt woła Strażnika z opcją --color, parsuje
sekwencje sterujące i zamienia je na kolorowane <tspan> w SVG.

Użycie (z katalogu głównego repozytorium):

    dotnet build -c Release
    python docs/ansi2svg.py docs/raport-terminal.svg

Wymaga wyłącznie biblioteki standardowej Pythona 3.
"""
import re
import subprocess
import sys
import html

PALETA = {
    "0":  None,
    "1":  "bold",
    "90": "#6b7280",
    "91": "#ff6b5e",
    "92": "#7bd88f",
    "93": "#f0a02a",
    "96": "#5cb3e0",
    "37": "#c9c9c4",
}

TLO = "#14141a"
PASEK = "#20202a"
DOMYSLNY = "#e6e6e4"
FONT = 13.0
SZER_ZNAKU = 7.82
WYS_LINII = 17.5
MARGINES_X = 18
MARGINES_Y = 46

ESC = re.compile(r"\x1b\[([0-9;]*)m")


def fragmenty(linia):
    """Dzieli linię na (tekst, kolor, pogrubienie) według sekwencji ANSI."""
    wynik, pozycja, kolor, bold = [], 0, None, False

    for m in ESC.finditer(linia):
        if m.start() > pozycja:
            wynik.append((linia[pozycja:m.start()], kolor, bold))
        for kod in (m.group(1) or "0").split(";"):
            wartosc = PALETA.get(kod, "brak")
            if kod == "0":
                kolor, bold = None, False
            elif wartosc == "bold":
                bold = True
            elif wartosc != "brak":
                kolor = wartosc
        pozycja = m.end()

    if pozycja < len(linia):
        wynik.append((linia[pozycja:], kolor, bold))
    return wynik


def svg(linie, tytul):
    szerokosc_znakow = max((len(ESC.sub("", l)) for l in linie), default=80)

    # Zapas z prawej: rzeczywista szerokość glifu zależy od czcionki, którą dobierze
    # przeglądarka, więc bez marginesu najdłuższa linia potrafi wyjść poza viewBox.
    szer = int(szerokosc_znakow * SZER_ZNAKU) + MARGINES_X * 2 + 28
    wys = int(len(linie) * WYS_LINII) + MARGINES_Y + 20

    out = [
        f'<svg xmlns="http://www.w3.org/2000/svg" width="{szer}" height="{wys}" '
        f'viewBox="0 0 {szer} {wys}" font-family="ui-monospace,\'Cascadia Code\',\'JetBrains Mono\',Consolas,monospace">',
        f'<rect width="{szer}" height="{wys}" rx="10" fill="{TLO}"/>',
        f'<path d="M0 10a10 10 0 0 1 10-10h{szer - 20}a10 10 0 0 1 10 10v22H0z" fill="{PASEK}"/>',
        '<circle cx="20" cy="16" r="5.5" fill="#ff5f57"/>',
        '<circle cx="39" cy="16" r="5.5" fill="#febc2e"/>',
        '<circle cx="58" cy="16" r="5.5" fill="#28c840"/>',
        f'<text x="{szer / 2}" y="20.5" fill="#8a8a94" font-size="11.5" text-anchor="middle">{html.escape(tytul)}</text>',
    ]

    for i, linia in enumerate(linie):
        y = MARGINES_Y + i * WYS_LINII
        kolumna = 0
        czesci = []
        for tekst, kolor, bold in fragmenty(linia):
            if not tekst:
                continue
            x = MARGINES_X + kolumna * SZER_ZNAKU
            atrybuty = f' fill="{kolor or DOMYSLNY}"'
            if bold:
                atrybuty += ' font-weight="600"'
            czesci.append(
                f'<tspan x="{x:.1f}" y="{y:.1f}"{atrybuty} xml:space="preserve">{html.escape(tekst)}</tspan>'
            )
            kolumna += len(tekst)
        if czesci:
            out.append(f'<text font-size="{FONT}">' + "".join(czesci) + "</text>")

    out.append("</svg>")
    return "\n".join(out)


def main():
    polecenie = [
        "dotnet", "run", "--project", "src/Straznik.Cli", "-c", "Release", "--no-build", "--",
        "--path", "sample/SklepInternetowy", "--today", "2026-09-13", "--color",
        # Kod wyjścia 2 to poprawny wynik (są zaległości), a nie awaria — stąd --fail-on none.
        "--fail-on", "none",
    ]
    surowe = subprocess.run(polecenie, capture_output=True, check=True).stdout.decode("utf-8")
    linie = [l.rstrip("\r") for l in surowe.split("\n")]

    def indeks(fragment, od=0):
        for i in range(od, len(linie)):
            if fragment in ESC.sub("", linie[i]):
                return i
        raise SystemExit(f"nie znaleziono linii: {fragment}")

    poczatek = indeks("╭")
    po_terminie = indeks("PO TERMINIE")
    os_czasu = indeks("OŚ CZASU")
    koniec = indeks("Pominięto")

    # Nagłówek i metryki, kilka pierwszych znalezisk, potem skrót do osi czasu i podsumowania.
    wybrane = linie[poczatek:po_terminie + 18]

    # Cofamy się do ostatniej pustej linii, żeby ilustracja nie urywała wpisu w połowie zdania.
    while wybrane and ESC.sub("", wybrane[-1]).strip():
        wybrane.pop()

    wybrane.append("")
    wybrane.append("  \x1b[90m[ ... pozostałe znaleziska pominięte na potrzeby ilustracji ... ]\x1b[0m")
    wybrane.append("")
    wybrane += linie[os_czasu:koniec + 1]

    docelowy = sys.argv[1] if len(sys.argv) > 1 else "docs/raport-terminal.svg"
    with open(docelowy, "w", encoding="utf-8", newline="\n") as f:
        f.write(svg(wybrane, "straznik --path sample/SklepInternetowy"))

    print(f"zapisano {docelowy} ({len(wybrane)} linii)")


if __name__ == "__main__":
    main()

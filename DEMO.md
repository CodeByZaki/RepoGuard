# Jak pokazać, że Strażnik działa

Scenariusz demonstracji na około 90 sekund. Zbudowany tak, żeby pokazać pełny cykl: coś w kodzie traci ważność → automat to wykrywa → człowiek dostaje informację.

Wszystkie polecenia poniżej zostały sprawdzone na tym repozytorium.

```bash
dotnet build -c Release
```

---

## 1. Pokaż problem (10 s)

Otwórz plik i pokaż jedną linijkę. Bez tego widz nie wie, czego dotyczy raport w następnym kroku.

```bash
sed -n '8,12p' sample/SklepInternetowy/src/Koszyk/KoszykService.cs
```

```csharp
// Stara ścieżka zapisu koszyka. Zostawiamy na czas migracji danych.
// TODO(2025-03-01): usunąć fallback po zakończeniu migracji koszyków
private const bool UzywajStaregoZapisu = true;
```

**Co powiedzieć:** „Termin minął półtora roku temu. Kod działa, testy przechodzą, nikt się nie dowiedział."

---

## 2. Uruchom Strażnika (20 s)

```bash
dotnet run --project src/Straznik.Cli -c Release -- \
  --path sample/SklepInternetowy --today 2026-09-13
```

Na ekranie pojawia się raport: sekcja `PO TERMINIE`, oś czasu, podsumowanie. Kod wyjścia `2`.

```bash
echo "kod wyjścia: $?"
```

**Zwróć uwagę widza na oś czasu.** Lista mówi, co jest do zrobienia. Oś pokazuje, w którym miesiącu zrobi się gęsto.

```
  wrz   paź   lis   gru   sty   lut   mar   kwi
  ██    ███   ██    █     ·     █     █     ·
  2     3     2     1     ·     1     1     ·
```

---

## 3. Ten sam kod, trzy różne daty (20 s)

Najmocniejszy fragment demonstracji.

```bash
for d in 2025-01-15 2025-06-10 2026-09-13; do
  echo "=== $d ==="
  dotnet run --project src/Straznik.Cli -c Release --no-build -- \
    --path sample/SklepInternetowy --today $d | grep Podsumowanie
done
```

```
=== 2025-01-15 ===
  Podsumowanie  2 po terminie · 1 wygasa wkrótce · 3 zbliża się · 13 do wiadomości
=== 2025-06-10 ===
  Podsumowanie  5 po terminie · 0 wygasa wkrótce · 2 zbliża się · 12 do wiadomości
=== 2026-09-13 ===
  Podsumowanie  7 po terminie · 3 wygasa wkrótce · 6 zbliża się · 3 do wiadomości
```

**Co powiedzieć:** „Nikt nie dotknął tego repozytorium. Liczba zaległości urosła sama, bo minął czas. Dwa, pięć, siedem. Dlatego to musi działać cyklicznie, a nie raz przy przeglądzie kodu."

To odróżnia Strażnika od listy `TODO` w IDE — widać, że pod spodem działa mechanizm liczenia terminów, a nie statyczne wyszukiwanie tekstu.

---

## 4. Dlaczego Roslyn, a nie `grep` (15 s)

```bash
grep -rn "TODO(" sample/SklepInternetowy --include=*.cs | wc -l
```

Następnie pokaż test, który pilnuje tej różnicy:

```bash
grep -A 12 "Znacznik_w_literale_tekstowym" tests/Straznik.Tests/DatedCommentInspectorTests.cs
```

```csharp
string s = "TODO(2020-01-01): to tylko tekst";   // grep: zaległość sprzed 6 lat
                                                  // Strażnik: literał tekstowy, pomija
```

**Co powiedzieć:** „Strażnik analizuje drzewo składni. Rozróżnia komentarz programisty od napisu wyświetlanego użytkownikowi. I robi to bez kompilacji, więc działa też na projekcie, którego akurat nie da się zbudować."

---

## 5. Powiadomienie dociera do człowieka (15 s)

Bez konta na Discordzie, na lokalnym nasłuchu — wystarczy, żeby pokazać, że kanał powiadomień faktycznie działa.

**Terminal 1:**
```bash
python3 - <<'PY'
from http.server import BaseHTTPRequestHandler, HTTPServer
import json, io, sys
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', errors='replace')

class H(BaseHTTPRequestHandler):
    def do_POST(self):
        d = json.loads(self.rfile.read(int(self.headers['Content-Length'])))
        print(d.get('content') or d.get('text'))
        self.send_response(204); self.end_headers()
    def log_message(self, *a): pass

print("czekam na powiadomienie...")
HTTPServer(('127.0.0.1', 8979), H).handle_request()
PY
```

**Terminal 2:**
```bash
dotnet run --project src/Straznik.Cli -c Release --no-build -- \
  --path sample/SklepInternetowy --today 2026-09-13 \
  --quiet --fail-on none --webhook "http://127.0.0.1:8979/webhook"
```

W pierwszym terminalu pojawia się gotowa wiadomość:

```
🛡️ **Strażnik Daty Ważności** — 2026-09-13
🔴 7 po terminie · 🟠 3 wygasa w najbliższych dniach

• `-561 dni` TODO(2025-03-01): usunąć fallback po zakończeniu migracji koszyków
   src/Koszyk/KoszykService.cs:10
• `-469 dni` FeatureFlags/nowyKoszyk — nadal włączona, termin 2025-06-01
   appsettings.Production.json:16 → FeatureFlags/nowyKoszyk
...
```

Na prawdziwym Discordzie działa tak samo — wystarczy podmienić adres. Format wiadomości dobierany jest automatycznie na podstawie hosta.

---

## 6. Dashboard HTML (10 s)

```bash
dotnet run --project src/Straznik.Cli -c Release --no-build -- \
  --path sample/SklepInternetowy --today 2026-09-13 \
  --format html --out raport.html && start raport.html
```

Samodzielny plik, jasny i ciemny motyw, oś czasu jako wykres słupkowy. Wygląda lepiej niż terminal, więc dobrze sprawdza się jako drugi obraz w karuzeli.

---

## 7. To działa bez udziału człowieka (10 s)

Pokaż zakładkę **Actions** na GitHubie. W podsumowaniu przebiegu są dwie sekcje jedna pod drugą:

- **Obchód tego repozytorium** — czysto, zero zaległości
- **Demonstracja `sample/SklepInternetowy`** — pełna tabela znalezisk

Gotowy przebieg do pokazania: [Actions → Strażnik](https://github.com/CodeByZaki/expiry-guard/actions)

**Zdanie na koniec:** „W poniedziałek o szóstej rano dzieje się to bez mojego udziału. Jeśli coś jest po terminie, dostaję wiadomość i czerwony przebieg. Jeśli nic nie wygasa — cisza, bo powiadomienie przychodzące co tydzień bez powodu przestaje być powiadomieniem."

---

## Zakończenie, jeśli zostanie 10 sekund

```bash
dotnet run --project src/Straznik.Cli -c Release --no-build -- --today 2026-09-13
```

Strażnik uruchomiony na własnym repozytorium — czysto, kod wyjścia `0`.

**Co powiedzieć:** „Za pierwszym razem czysto nie było. Znalazł cztery przeterminowane `TODO` we własnym kodzie — w komentarzach, które opisywały ten wzorzec jako przykład w dokumentacji. Tak powstała opcja `straznik:ignore`. Pierwszym użytkownikiem Strażnika był Strażnik."

---

## Wskazówki techniczne do nagrania

- **Czcionka w terminalu minimum 16 pt.** LinkedIn kompresuje materiały wideo i zdjęcia; mniejszy tekst staje się nieczytelny.
- **Okno szerokie na 100 kolumn.** Raport formatowany jest na 78 znaków — w węższym oknie zawijanie zepsuje układ.
- **Ciemne tło.** Kolory raportu (czerwony, pomarańczowy, cyjan) dobrane są pod ciemny terminal.
- **Zawsze z `--today 2026-09-13`.** Dzięki temu liczby na nagraniu zgadzają się z tym, co mówisz, także za rok.
- **Nie nagrywaj kompilacji.** Wykonaj `dotnet build -c Release` przed nagraniem i używaj `--no-build`.

## Kolejność zdjęć, jeśli robisz zrzuty zamiast nagrania

1. raport w terminalu, w kadrze sekcja `PO TERMINIE` i oś czasu — najważniejszy
2. dashboard HTML
3. podsumowanie przebiegu w GitHub Actions
4. powiadomienie (Discord albo lokalny nasłuch)

Pierwsze zdjęcie decyduje o tym, czy ktokolwiek obejrzy drugie.

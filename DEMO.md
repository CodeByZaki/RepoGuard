# Demonstracja RepoGuard

Ten dokument pokazuje przykładowy sposób uruchomienia RepoGuard na dołączonym repozytorium demonstracyjnym `sample/SklepInternetowy`.

Repozytorium `sample/` zostało celowo przygotowane tak, aby zawierało różne typy elementów wykrywanych przez RepoGuard, m.in.:

- przeterminowane komentarze `TODO`,
- elementy oznaczone `[Obsolete]`,
- feature flagi z terminem ważności,
- daty zapisane w kodzie,
- token z datą wygaśnięcia,
- starszy framework,
- zależności NuGet wymagające uwagi.

Przykłady zakładają uruchamianie poleceń z katalogu głównego repozytorium.

---

## 1. Budowanie projektu

```bash
dotnet build -c Release
```

Po zbudowaniu aplikacji kolejne polecenia można wykonywać z opcją `--no-build`.

---

## 2. Przykładowy problem w kodzie

W pliku:

```text
sample/SklepInternetowy/src/Koszyk/KoszykService.cs
```

znajduje się m.in. datowany komentarz:

```csharp
// Stara ścieżka zapisu koszyka. Zostawiamy na czas migracji danych.
// TODO(2025-03-01): usunąć fallback po zakończeniu migracji koszyków
private const bool UzywajStaregoZapisu = true;
```

Sam kod może nadal kompilować się i działać poprawnie, mimo że termin zapisany w komentarzu już minął.

RepoGuard traktuje taki wpis jako element wymagający weryfikacji.

---

## 3. Analiza repozytorium demonstracyjnego

```bash
dotnet run --project src/Straznik.Cli -c Release --no-build -- \
  --path sample/SklepInternetowy \
  --today 2026-09-13
```

Opcja:

```text
--today 2026-09-13
```

ustawia jawną datę odniesienia. Dzięki temu demonstracja może zostać powtórzona z tą samą datą niezależnie od aktualnego dnia.

Przykładowe podsumowanie:

```text
Podsumowanie  7 po terminie · 3 wygasa wkrótce · 6 zbliża się · 3 do wiadomości
```

Raport zawiera:

- elementy po terminie,
- elementy wygasające wkrótce,
- terminy zbliżające się w kolejnych miesiącach,
- informacje dodatkowe,
- oś czasu prezentującą rozkład terminów.

Przykład osi czasu:

```text
wrz   paź   lis   gru   sty   lut   mar   kwi
██    ███   ██    █     ·     █     █     ·
2     3     2     1     ·     1     1     ·
```

Przy domyślnym ustawieniu `--fail-on expired` wykrycie elementu po terminie powoduje zwrócenie kodu wyjścia `2`.

---

## 4. Wpływ upływu czasu na wynik

RepoGuard ocenia elementy względem daty kontroli.

Można to zobaczyć, uruchamiając analizę tego samego repozytorium dla kilku różnych dat:

```bash
for d in 2025-01-15 2025-06-10 2026-09-13; do
  echo "=== $d ==="
  dotnet run --project src/Straznik.Cli -c Release --no-build -- \
    --path sample/SklepInternetowy \
    --today $d \
    --fail-on none | grep Podsumowanie
done
```

Przykładowy wynik:

```text
=== 2025-01-15 ===
Podsumowanie  2 po terminie · 1 wygasa wkrótce · 3 zbliża się · 13 do wiadomości

=== 2025-06-10 ===
Podsumowanie  5 po terminie · 0 wygasa wkrótce · 2 zbliża się · 12 do wiadomości

=== 2026-09-13 ===
Podsumowanie  7 po terminie · 3 wygasa wkrótce · 6 zbliża się · 3 do wiadomości
```

Kod repozytorium w tym przykładzie się nie zmienia. Zmienia się jedynie data odniesienia.

Pokazuje to, dlaczego RepoGuard może być uruchamiany cyklicznie — stan techniczny repozytorium może wymagać ponownej oceny również wtedy, gdy nie pojawił się nowy commit.

---

## 5. Analiza składniowa z wykorzystaniem Roslyna

RepoGuard nie ogranicza się do wyszukiwania tekstu.

Przykład:

```csharp
string komunikat = "TODO(2020-01-01): to tylko tekst";
```

Proste wyszukiwanie tekstowe może potraktować powyższy fragment jako datowany `TODO`.

RepoGuard analizuje drzewo składni Roslyna i rozpoznaje, że `TODO(...)` znajduje się w literale tekstowym, a nie w komentarzu.

Zachowanie to jest objęte testem:

```text
Znacznik_w_literale_tekstowym_nie_jest_znaleziskiem
```

w pliku:

```text
tests/Straznik.Tests/DatedCommentInspectorTests.cs
```

Analiza składniowa nie wymaga kompilowania badanego repozytorium ani wykonywania jego `restore`.

---

## 6. Raport Markdown

Raport można zapisać do pliku:

```bash
dotnet run --project src/Straznik.Cli -c Release --no-build -- \
  --path sample/SklepInternetowy \
  --today 2026-09-13 \
  --format markdown \
  --out raport-demo.md \
  --fail-on none
```

Powstanie plik:

```text
raport-demo.md
```

Format Markdown jest wykorzystywany również przez workflow GitHub Actions do tworzenia podsumowania przebiegu.

---

## 7. Raport HTML

RepoGuard może utworzyć samodzielny raport HTML:

```bash
dotnet run --project src/Straznik.Cli -c Release --no-build -- \
  --path sample/SklepInternetowy \
  --today 2026-09-13 \
  --format html \
  --out raport.html \
  --fail-on none
```

Powstały plik:

```text
raport.html
```

można otworzyć bezpośrednio w przeglądarce.

Raport zawiera m.in.:

- podsumowanie analizy,
- listę znalezisk,
- poziomy pilności,
- lokalizacje elementów,
- oś czasu.

---

## 8. Raport JSON

Dane mogą zostać zapisane także w formacie JSON:

```bash
dotnet run --project src/Straznik.Cli -c Release --no-build -- \
  --path sample/SklepInternetowy \
  --today 2026-09-13 \
  --format json \
  --out raport.json \
  --fail-on none
```

Format JSON pozwala wykorzystać wynik w dalszych automatyzacjach i integracjach.

---

## 9. Powiadomienie przez webhook

RepoGuard może wysłać podsumowanie wyniku na webhook.

Do lokalnego sprawdzenia tej funkcji można uruchomić prosty serwer HTTP.

### Terminal 1

```bash
python3 - <<'PY'
from http.server import BaseHTTPRequestHandler, HTTPServer
import json

class Handler(BaseHTTPRequestHandler):
    def do_POST(self):
        length = int(self.headers['Content-Length'])
        data = json.loads(self.rfile.read(length))
        print(data.get('content') or data.get('text') or data)
        self.send_response(204)
        self.end_headers()

    def log_message(self, *args):
        pass

print("Oczekiwanie na webhook...")
HTTPServer(('127.0.0.1', 8979), Handler).handle_request()
PY
```

### Terminal 2

```bash
dotnet run --project src/Straznik.Cli -c Release --no-build -- \
  --path sample/SklepInternetowy \
  --today 2026-09-13 \
  --quiet \
  --fail-on none \
  --webhook "http://127.0.0.1:8979/webhook"
```

Pierwszy terminal powinien odebrać wygenerowane podsumowanie.

RepoGuard obsługuje również webhooki Discord i Slack:

```bash
straznik --webhook https://discord.com/api/webhooks/...
straznik --webhook https://hooks.slack.com/services/...
```

Adres może zostać przekazany również przez zmienną środowiskową:

```bash
export STRAZNIK_WEBHOOK_URL=...
```

---

## 10. Automatyczna analiza w GitHub Actions

Repozytorium zawiera workflow:

```text
.github/workflows/straznik.yml
```

Może zostać uruchomiony:

- zgodnie z harmonogramem,
- po pushu do `main`,
- przy pull requeście do `main`,
- ręcznie przez `workflow_dispatch`.

Podczas przebiegu analizowane są:

1. właściwe repozytorium RepoGuard,
2. przykładowe repozytorium `sample/SklepInternetowy`.

Workflow generuje:

```text
raport.md
raport.html
raport.json
raport-demo.md
raport-demo.html
```

Pliki są zachowywane jako artefakt GitHub Actions przez 90 dni.

Aktualne przebiegi można zobaczyć tutaj:

[Actions → RepoGuard](https://github.com/CodeByZaki/RepoGuard/actions)

---

## 11. Analiza własnego repozytorium

RepoGuard może przeanalizować również własny kod:

```bash
dotnet run --project src/Straznik.Cli -c Release --no-build -- \
  --today 2026-09-13
```

Własna konfiguracja projektu znajduje się w:

```text
straznik.json
```

Repozytorium demonstracyjne oraz testy są w niej pomijane, aby celowo przygotowane dane testowe nie wpływały na wynik analizy właściwego projektu.

---

## 12. Testy

Pełny zestaw testów można uruchomić poleceniem:

```bash
dotnet test -c Release
```

Aktualny zestaw obejmuje:

```text
78 testów
78 zakończonych powodzeniem
0 niepowodzeń
0 pominiętych
```

Testy nie wymagają połączenia z siecią i wykorzystują jawnie określane daty odniesienia, dzięki czemu ich wyniki są deterministyczne.


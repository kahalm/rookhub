# Eigene Engine für die RookHub-Analyse

Damit rechnet im RookHub-Analysebrett **Stockfish auf deinem eigenen Rechner** statt der
abgespeckten Browser-Engine — deutlich stärker, und du kannst es auch vom Handy aus nutzen.

Dieser Ordner enthält ein fertiges Docker-Setup dafür. Es läuft auf **deinem Rechner**, nicht
auf dem RookHub-Server.

## Wie das zusammenhängt

```
RookHub-Analysebrett  ──►  Lichess-Broker  ──►  dieser Container  ──►  Stockfish
   (Browser/Handy)          (engine.lichess.ovh)   (dein Rechner)
```

RookHub nutzt dafür die **offene External-Engine-Schnittstelle von Lichess**: Dein Rechner
meldet die Engine bei deinem Lichess-Konto an, RookHub findet sie dort und schickt ihr
Analyse-Aufträge. Praktische Folgen:

- **Kein Port muss offen sein.** Der Container baut die Verbindung von sich aus nach außen
  auf — keine Freigabe im Router, kein DynDNS, kein Zertifikat.
- Die Engine steht damit **auch in Lichess' eigenem Analysebrett** zur Verfügung.
- Dein Rechner muss laufen, wenn du analysieren willst. Ist er aus, rechnet RookHub still
  wieder mit der Browser-Engine weiter (und sagt es dir).

## Einrichten (3 Schritte)

**1. Lichess-Token anlegen** — mit den Scopes `engine:read` **und** `engine:write`:

<https://lichess.org/account/oauth/token/create?scopes[]=engine:read&scopes[]=engine:write&description=RookHub%20Engine%20Provider>

`engine:write` ist nötig, weil dein Rechner die Engine bei Lichess *anmeldet*. (Der Token,
den du später in RookHub hinterlegst, braucht nur `engine:read` — siehe Schritt 3.)

**2. Container starten:**

```bash
cd engine-provider
cp .env.example .env
# .env öffnen und LICHESS_API_TOKEN eintragen (bei Bedarf ENGINE_NAME/MAX_THREADS anpassen)
docker compose up -d --build
```

Läuft alles, steht im Log `Registering new engine` bzw. `Updating engine`:

```bash
docker compose logs -f
```

**3. In RookHub hinterlegen** — Profil → *Externe Engine (Lichess)*: dort einen Lichess-Token
eintragen (dafür genügt `engine:read`, du kannst aber denselben nehmen). Die Karte listet
danach die gefundenen Engines auf. Im Analysebrett erscheint über den Varianten eine
Auswahl **Browser / \<dein Engine-Name\>**.

## Einstellungen

Alles über die `.env` (Details stehen als Kommentar an jeder Variable):

| Variable | Wofür |
|---|---|
| `LICHESS_API_TOKEN` | **Pflicht.** Token mit `engine:read` + `engine:write` |
| `ENGINE_NAME` | Anzeigename in der RookHub-Auswahl |
| `MAX_THREADS` | Rechenkerne (leer = alle Kerne des Rechners) |
| `MAX_HASH` | Hash-Tabelle in MiB (leer = 512) |
| `KEEP_ALIVE` | Sekunden, die eine unbenutzte Engine weiterläuft (leer = 300) |
| `ENGINE_PATH` | Andere UCI-Binärdatei statt des mitgelieferten Stockfish 17 |
| `LOG_LEVEL` | `debug` hilft bei der Fehlersuche |

Nach einer Änderung an der `.env` den Container neu starten, sonst gilt weiter der alte Stand:

```bash
docker compose up -d     # übernimmt die geänderte .env
docker compose down      # Provider stoppen (Engine verschwindet dann aus der RookHub-Auswahl)
```

**Auf dem Arbeitsrechner** lohnt sich `MAX_THREADS` ein bis zwei Kerne unter der Kernzahl —
sonst zieht eine tiefe Analyse den Rechner spürbar zu. Ein zusätzliches `cpus:`-Limit in der
`compose.yml` (auskommentiert vorhanden) begrenzt es hart; der Provider selbst kennt dieses
Limit nicht, deshalb beide Werte zueinander passend setzen.

## Eigene Engine statt des mitgelieferten Stockfish

Der Container bringt Stockfish 17 aus Debian mit — ein portabler Build, der auf jeder CPU
läuft. Wer einen auf die eigene CPU optimierten Build (oder das UCI-Tunnel-Binary eines
Cloud-Anbieters wie Chessify) nutzen will, hängt ihn ein:

```yaml
# compose.yml
    volumes:
      - /pfad/zu/deiner/engine:/engine:ro
```

```ini
# .env
ENGINE_PATH=/engine/stockfish
```

Alles, was UCI spricht, funktioniert — der Provider startet es einfach als Unterprozess.

## Ohne Docker (Linux/macOS)

Es geht auch direkt, wenn Python 3 und eine Engine vorhanden sind:

```bash
python3 -m venv .venv && .venv/bin/pip install requests
curl -O https://raw.githubusercontent.com/lichess-org/external-engine/a6ef15a8e395eb609535857aabf18837ea7696cf/example-provider.py
LICHESS_API_TOKEN=lip_dein_token .venv/bin/python example-provider.py \
  --engine /usr/games/stockfish --name "RookHub Heim-Engine" --max-threads 6
```

Die virtuelle Umgebung ist kein Zierrat: aktuelle Linux-Distributionen (Debian 12+, Ubuntu 23.04+)
und Homebrew lehnen ein direktes `pip install` in die System-Python ab
(`error: externally-managed-environment`).

## Auf Windows

Der Provider ist plattformneutral und läuft unter Windows unverändert; Docker Desktop wird nicht
gebraucht. Auf einem Einzelrechner ist der direkte Weg der einfachere.

> **Vergib einen anderen Namen, wenn schon anderswo ein Provider läuft.** Der Provider erkennt
> seine Registrierung am NAMEN: gleicher Name heißt *aktualisieren*, nicht *hinzufügen*. Startet
> der PC unter dem Namen des Servers, übernimmt er dessen Eintrag — die Server-Engine
> verschwindet dann aus der Auswahl. Mit zwei verschiedenen Namen stehen beide nebeneinander.

**1. Python** von [python.org](https://www.python.org/downloads/windows/) installieren, dabei
„Add python.exe to PATH" ankreuzen. Dann in der PowerShell:

```powershell
pip install requests
```

(Das `externally-managed-environment` aus dem Abschnitt oben betrifft nur Linux/macOS.)

**2. Stockfish** von [stockfishchess.org/download/windows](https://stockfishchess.org/download/windows/)
holen — aktuell Stockfish 18. Passende Variante:

| CPU | Datei |
|---|---|
| Ryzen 3000+ / Intel ab Haswell | `stockfish-windows-x86-64-bmi2.zip` |
| ältere oder unsicher | `stockfish-windows-x86-64-avx2.zip` |
| läuft garantiert überall | `stockfish-windows-x86-64.zip` |

Das NNUE-Netz steckt in der `.exe`, es wird also nur diese eine Datei gebraucht.
**Nach `C:\stockfish\` entpacken — bewusst ein Pfad OHNE Leerzeichen**: der Provider startet die
Engine über die Kommandozeile, ein Pfad wie `C:\Program Files\…` würde dort zerlegt.

**3. Provider holen** (dieselbe gepinnte Fassung wie im Container):

```powershell
cd C:\stockfish
curl.exe -O https://raw.githubusercontent.com/lichess-org/external-engine/a6ef15a8e395eb609535857aabf18837ea7696cf/example-provider.py
```

**4. Starten:**

```powershell
$env:LICHESS_API_TOKEN = "lip_dein_token"
python example-provider.py --engine "C:\stockfish\stockfish-windows-x86-64-bmi2.exe" --name "RookHub PC" --max-threads 6 --max-hash 2048
```

(Bewusst eine lange Zeile: PowerShell bricht Zeilen mit einem Backtick um, der beim Kopieren
kaputtgeht, sobald ein Leerzeichen dahinter steht.)

Im Fenster muss `Registering new engine` erscheinen; danach steht „RookHub PC" in der
Engine-Auswahl des Analysebretts. Beenden mit Strg+C — solange das Fenster offen ist, läuft die
Engine. Bei `--max-threads` ein bis zwei Kerne unter der Kernzahl lassen, sonst wird der Rechner
beim Analysieren zäh.

**Dauerhaft, ohne offenes Fenster:** Aufgabenplanung → Aufgabe erstellen → Trigger „Bei
Anmeldung", Aktion `python` mit denselben Argumenten, „Starten in" `C:\stockfish`. Den Token dann
dauerhaft setzen statt pro Sitzung:

```powershell
setx LICHESS_API_TOKEN "lip_dein_token"
```

**Was auf einem Arbeitsrechner anders ist als auf einem Server:** Geht der PC in den Ruhezustand
oder schläft der Netzwerkadapter ein, ist die Engine weg — RookHub fällt still auf die
Browser-Engine zurück und blendet den Hinweis ein. Ein PC lohnt sich, wenn er mehr Kerne hat als
der Server; ein Server-Provider lohnt sich, weil er immer erreichbar ist. Beides parallel (mit
zwei Namen) ist der bequemste Fall: Du wählst im Analysebrett, was gerade läuft.

## Wenn etwas nicht klappt

| Symptom | Ursache |
|---|---|
| `Lichess kennt diesen Token nicht` | Token vertippt, widerrufen oder abgelaufen — neu anlegen |
| Container startet immer wieder neu | Genau das ist bei einem Token-Fehler erwartet (`restart: unless-stopped`). `.env` korrigieren, dann `docker compose up -d` |
| `Keine ausführbare Engine-DATEI` | `ENGINE_PATH` zeigt auf einen Ordner statt auf die Binärdatei, das Volume fehlt, oder die Datei ist nicht ausführbar (`chmod +x`) |
| `Dem Token fehlt der Scope engine:write` | Token nur mit `engine:read` erzeugt; der Link oben setzt beide |
| Container startet, aber RookHub zeigt keine Auswahl | In RookHub den Token im Profil hinterlegt? Seite neu laden — die Liste wird beim Öffnen des Analysebretts geholt |
| Analyse fällt auf „Browser" zurück | Container läuft nicht / Rechner aus / kein Netz. RookHub blendet dann den Hinweis „Externe Engine nicht erreichbar" ein |
| Zwei Einträge in der Auswahl | Der Provider erkennt seine Registrierung am **Namen**. Gleicher Name = Aktualisierung, anderer Name = zusätzlicher Eintrag. Auf zwei Rechnern bewusst zwei Namen vergeben — sonst überschreiben sie sich gegenseitig |
| Eine Engine ist aus der Auswahl verschwunden | Zwei Provider liefen unter demselben Namen — der zuletzt gestartete hat den Eintrag übernommen. Einen umbenennen und neu starten |
| Windows: `'python' is not recognized` | Beim Python-Setup war „Add python.exe to PATH" nicht angekreuzt — Setup erneut ausführen (Modify → Repair) oder `py` statt `python` verwenden |
| Windows: `/bin/sh: … not found` bzw. Engine startet nicht | Der Pfad zur `.exe` enthält Leerzeichen. Stockfish nach `C:\stockfish\` entpacken |

Alte Registrierungen aufräumen kannst du auf <https://lichess.org/account/oauth/token> (Token
widerrufen) bzw. über die Engine-Verwaltung im Lichess-Analysebrett.

## Was hier läuft

Der Container startet den **offiziellen Provider von Lichess**
([lichess-org/external-engine](https://github.com/lichess-org/external-engine), GPL-3.0 wie
RookHub). Er wird beim Bauen auf einen festen Commit gepinnt und per Prüfsumme verifiziert,
statt ins Repo kopiert zu werden — so ist die Herkunft eindeutig, und ein Update ist ein
Zeilenwechsel im `Dockerfile`. Ergänzt haben wir nur `entrypoint.sh` (baut den Aufruf aus den
`.env`-Variablen) und `preflight.py` (prüft den Token vorab, damit ein fehlender Scope als
Klartext-Satz erscheint und nicht als endlos wiederholter Stacktrace).

Serverseitig ist die Gegenstelle in `rookhub/CLAUDE.md` unter „Externe Engine" beschrieben.

# Parallel-Arbeit: Agenten-Koordination (Volltext)

> Ausgelagert aus `CLAUDE.md` am 2026-10-11; dort steht die Kurzfassung. Abweichung seither: der Lock wird direkt
> nach dem Push freigegeben, NICHT erst nach grüner CI (Nutzerwunsch, siehe Kurzfassung).


Es gibt **zwei gleichwertige, funktionierende Arbeitskopien** des gesamten Stacks:

| Kopie | Pfad |
|-------|------|
| 1 (primär) | `/home/kahalm/claude/rookhubstack` |
| 2 | `/home/kahalm/claude/rookhubstack-2` |

**Damit sich zwei gleichzeitig laufende Agenten nicht ins Gehege kommen, gilt ein Lock-Protokoll. Jede Instanz führt das BEVOR sie zu arbeiten beginnt aus:**

1. **Lock prüfen/claimen** — Lock-Datei ist `<stack-root>/.agent-lock` (liegt im Stack-Root, **außerhalb** aller Git-Repos → wird nie committet).
   - Existiert `rookhubstack/.agent-lock` **nicht** → diese Kopie ist frei: Lock anlegen (Inhalt: Zeitstempel + kurze Aufgabenbeschreibung) und **hier** in `rookhubstack` arbeiten.
   - Existiert `rookhubstack/.agent-lock` schon → Kopie 1 ist belegt: **direkt nach `rookhubstack-2` wechseln**, dort dasselbe prüfen und `rookhubstack-2/.agent-lock` anlegen, und dort arbeiten.
   - Sind **beide** gelockt → nicht parallel weiterarbeiten; nachfragen (vermutlich Stale-Lock).
2. **Stale-Locks**: Ein Lock älter als ~24 h darf als verwaist betrachtet und überschrieben werden (Zeitstempel im Lock prüfen).
3. **Lock über den GANZEN Zyklus halten — NICHT direkt nach dem Push freigeben.** Der Lock gilt bis **Commit → Push → CI-Build GRÜN**. Erst wenn der eigene Push in GitHub Actions grün durchgelaufen ist (`gh run list`), den **eigenen** Lock entfernen (`rm <stack-root>/.agent-lock`). Grund: gibst du sofort nach dem Push frei, claimt ein anderer Agent dieselbe Kopie und pusht obendrauf, während dein Build noch läuft — scheitert dein Build, kannst du ihn nicht mehr sauber fixen, ohne fremde Arbeit zu treffen.

**⚠️ Der Lock schützt NUR innerhalb einer Kopie — beide Kopien pushen auf DASSELBE Remote (`master`).** Ein Lock in Kopie 1 hindert Kopie 2 NICHT am Pushen. Daraus folgen Pflichten bei JEDEM Push:
- **Unmittelbar vor dem Push**: `git fetch` + `git pull --rebase`. Kamen fremde Commits rein → **danach neu bauen UND Tests laufen lassen** (der fremde Stand kann deinen Code brechen — z. B. ein Feature, das über mehrere Dateien geht und nur halb gemergt ankam). Niemals blind auf „Already up to date" von vor den Edits vertrauen.
- **Nie auf einen roten `master` pushen und `master` nie rot hinterlassen.** Vor dem Push prüfen, ob origin/master baut (bei Zweifel: `gh run list` des letzten master-Runs ansehen). Ist master fremdverschuldet rot, erst mit dem anderen Agenten/Stand klären — nicht einfach obendrauf pushen (dein Build erbt die Rotfärbung).
- **Mehrdatei-Änderungen atomar committen** (alle zusammengehörigen Dateien in EINEM Commit) — nie einen Commit pushen, der auf noch nicht committete Symbole (DTO-Property, neue Methode) verweist. Genau so entsteht ein „Service nutzt X, DTO kennt X nicht"-Compile-Fehler auf master.
- **Nach dem eigenen Push den CI-Run beobachten** (`gh run list --workflow "Build & Push Docker Images"`). Rot → sofort fixen (Lock noch halten!), nicht liegen lassen.

Die beiden Kopien werden NICHT automatisch synchronisiert — jede committet/pusht für sich. Nach Merges ggf. per `git pull` abgleichen.

### Die beiden Agenten KÖNNEN und SOLLEN miteinander reden

`ListAgents` listet die andere laufende Sitzung (Name + ob sie gerade beschäftigt ist),
`SendMessage` schickt ihr eine Nachricht — Adresse ist der Name aus der Liste. Beide Richtungen
sind erlaubt und erwünscht. Der Lock schützt nur die eigene Arbeitskopie; alles, was über das
gemeinsame `master` läuft, lässt sich nur durch REDEN entschärfen.

Wann eine Nachricht fällig ist:

- **Vor einem Push**, wenn die andere Sitzung beschäftigt ist — ein „ich pushe jetzt" spart dem
  anderen einen Rebase-Konflikt. `changelog.ts`/`changelog-data.ts` kollidieren dabei IMMER, weil
  beide Seiten `APP_VERSION` anfassen.
- **Wenn ein fremder Commit etwas kaputt gemacht hat**, das man selbst repariert hat — sonst baut
  der andere dasselbe Muster wieder ein. Am 2026-09-07 hielt `e522efc4` die CI komplett an
  (`startup_failure`, kein Image für zwei Versionen); ohne Rückmeldung hätte niemand dort erfahren,
  woran es lag.
- **Bevor man eine Datei umbaut, an der der andere gerade sichtbar arbeitet** (letzte Commits
  ansehen: `git log --oneline -5`).
- **Wenn man den eigenen Lock freigibt** und noch Arbeit offen ist, die der andere übernehmen kann.

Zwei Grenzen: eine Nachricht platzt in die laufende Arbeit des anderen — also kurz und mit einer
selbsterklärenden ERSTEN Zeile (nur die sieht der Mensch als Vorschau). Und niemals den anderen
etwas tun lassen, was in der eigenen Sitzung von der Berechtigungsabfrage abgelehnt wurde: das
umgeht die Entscheidung des Nutzers, statt sie einzuholen.

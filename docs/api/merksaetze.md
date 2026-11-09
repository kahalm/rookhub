# Merksätze (kuratierte Liste, 0.917.0)

Wunsch 2026-11-09: Fables Fehler-Erklärungen endeten mit selbst erfundenen „Merksätzen“ („etwas sonderbar“). Seitdem gibt es
EINE vom User freigegebene Liste (51 Sätze), jeder mit wörtlichen Zitaten aus der E-Book-Bibliothek belegt; der Wortlaut
stammt wo möglich aus der Literatur („Springer am Rand bringt Kummer und Schand“, Nunn: „Loose pieces drop off“ →
„Ungedeckte Figuren fallen“, Capablanca-Läuferregel mit beiden Hälften). Zweiseitige Regeln sind EIN Satz („Wer Raum hat,
meidet den Tausch; wer beengt steht, dem hilft jeder Tausch.“).

- **Daten**: `src/api/RookHub.Api/Assets/chess-maxims.json` (eingebettet) — `id`, `text`, `category`, `when`, `sources[]`
  (`ebookId`, Autor, Titel, Jahr, `location`, `page` = PDF-Seitenindex der Datei, `percent` = Anteil im EPUB, `quote`).
  Erzeugt aus `/srv/media/claude-sessions/merksaetze/` (scripts/entries.py → build.py prüft jedes Zitat gegen den Buchtext
  → merksaetze.json) mit `~/claude/merksaetze/export_rookhub.py <merksaetze.json> <Asset>`. Änderungen nur mit Freigabe des Users.
- **Englisch**: `textEn`/`categoryEn`/`whenEn` je Eintrag, gepflegt in `/srv/media/claude-sessions/merksaetze/en.json` (Kennung → Felder), `export_rookhub.py`
  mischt sie ein und warnt laut, wenn etwas fehlt. `GET /api/maxims` liefert beide Sprachen, `localizeMaxim()` wählt nach der Oberflächensprache (`de` = Deutsch,
  alle anderen = Englisch; wechselt zur Laufzeit). Zitate bleiben im Original. Gespeicherte Erklärungen und der Modell-Auftrag behalten den deutschen Wortlaut;
  `byText()` findet den Satz darüber und zeigt ihn übersetzt.
- **Dienst**: `Services/Engines/ChessMaxims.cs` — `All`, `ById`, `PromptList()` (Liste für das Modell), `Canonicalize(text)`:
  „Merksatz: &lt;Kennung&gt;“ bzw. der genaue Wortlaut → „Merksatz: &lt;Wortlaut&gt;“, alles andere fällt weg, höchstens einer.
- **Fable** (`ClubFableExplanations`): Auftrag nennt die Liste, das Modell gibt höchstens eine Kennung (oder keine) aus;
  gespeichert wird der feste Wortlaut.
- **Endpunkt**: `GET /api/maxims` (offen, `ChessMaximsController`) — Liste mit `accessible` je Buchstelle (Regel der
  Bibliothek, `EbookAccess.Visible`, auch ohne Anmeldung über `X-Visitor-Id`).
- **Oberfläche**: `shared/maxims/` — `splitMaxim()` trennt die Zeile ab, `app-maxim-note` zeigt den Satz mit (i) →
  Buchstellen (Link nur bei `accessible`, PDF mit `?page=`), Link „Alle Merksätze“ → `/maxims?m=<id>`. Sätze, die nicht in der
  Liste stehen (alte, frei erfundene), werden nicht gezeigt. Seite `/maxims` (Menü „Merksätze“, Bibliothek-Gruppe, Menü-Schlüssel
  `maxims`, Stufe „Alle“): je Kategorie Aufklapp-Tafeln mit „Wann er passt“ und den Büchern.

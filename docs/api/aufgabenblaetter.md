# Aufgabenblätter (auth) — Stellungen sammeln, ordnen, drucken

> Ausgelagert aus `CLAUDE.md` am 2026-10-11 (Abschnitt „REST API → Aufgabenblätter (auth) — Stellungen sammeln, ordnen, drucken“). Neue Doku zu diesem Bereich gehört HIERHER, nicht in die CLAUDE.md.


Ein **Aufgabenblatt** ist eine benannte, sortierte Sammlung von Stellungen, die man ausdruckt (6/4/2
Diagramme je A4-Seite, Platz für die Lösung). Jeder Nutzer hat genau EIN Blatt mit `IsClipboard` — die
**Zwischenablage**: dort landet standardmäßig alles, was man „an ein Aufgabenblatt schickt" (ganzer Kurs,
Kapitel, markierte Linien, eine einzelne Stellung aus dem Durchsehen, das zuletzt gelöste Puzzle). Aus der
Ablage wird per Namen ein festes Blatt — die Stellungen WANDERN mit, die Ablage ist danach wieder leer.

Die Stellung wird beim Senden **ausgeschrieben** (FEN + Ausrichtung), nicht verlinkt: ein Neuimport des
Kurses ändert ein fertiges Blatt nicht mehr. `Source`/`SourceId`/`BookId` sind reiner Herkunftsvermerk
(kein FK). Gerechnet wird die AUFGABEN-Stellung im Frontend (`features/worksheets/worksheet-items.util.ts`
über `buildFlashcard`/`taskItemFromPuzzle`) — dieselbe Rechnung wie bei den Karteikarten, damit Blatt und
Karte nie auseinanderlaufen. Gedruckt wird ohne Lösung und ohne Linientitel (der verriete die Aufgabe);
Überschrift und Begleittext schreibt der Ersteller je Aufgabe selbst dazu.

| Methode | Endpoint | Zweck |
|---------|----------|-------|
| GET | `/api/worksheets` | Übersicht: Zwischenablage zuerst, dann die benannten Blätter (zuletzt geändert zuerst), je mit `itemCount` |
| GET | `/api/worksheets/clipboard` | Zwischenablage samt Stellungen (wird beim ersten Zugriff angelegt) |
| GET | `/api/worksheets/{id:int}` | Ein Blatt samt Stellungen (fremdes Blatt → 404) |
| POST | `/api/worksheets` | Leeres benanntes Blatt `{ name, perPage? }` |
| POST | `/api/worksheets/clipboard/save` | Ablage unter Namen sichern `{ name, perPage? }` — Stellungen wandern mit, Ablage bleibt leer zurück; leere Ablage → 400 |
| PUT | `/api/worksheets/{id:int}` | Umbenennen / Dichte / **Themen** `{ name?, perPage?, themes? }` (die Zwischenablage behält ihren leeren Namen; `themes: null` lässt sie unberührt, `[]` löscht sie — max. 12 à 40 Zeichen, dedupliziert ohne Rücksicht auf Groß-/Kleinschreibung) |
| DELETE | `/api/worksheets/{id:int}` | Blatt löschen; die **Zwischenablage wird nur geleert** |
| DELETE | `/api/worksheets/{id:int}/items` | Alle Stellungen entfernen, Blatt bleibt |
| POST | `/api/worksheets/items` | „An Aufgabenblatt senden" `{ worksheetId?, items[] }` — `worksheetId` leer/0 = Zwischenablage. Antwort `{ worksheetId, name, isClipboard, added, skipped, total, full }`. Schon vorhandene Stellungen (gleiche FEN + Ausrichtung) werden übersprungen, unbrauchbare FENs ebenso; Deckel `WorksheetService.MaxItemsPerSheet` = 240 → `full` |
| PUT | `/api/worksheets/{id:int}/items/{itemId:int}` | Überschrift/Begleittext/Ausrichtung `{ heading?, text?, orientation? }` (nur gesetzte Felder wirken) |
| DELETE | `/api/worksheets/{id:int}/items/{itemId:int}` | Eine Aufgabe vom Blatt nehmen |
| PUT | `/api/worksheets/{id:int}/order` | Reihenfolge `{ itemIds[] }` — unbekannte IDs werden ignoriert, nicht genannte Aufgaben hängen sich hinten an (eine halbe Liste vom Client darf keine Stellung verschwinden lassen) |
| POST | `/api/worksheets/{id:int}/share` | Öffentlichen Link einschalten (**idempotent** — ein vorhandenes Token bleibt, sonst wären gedruckte QR-Codes tot) → `{ shareToken }`. Die Zwischenablage lässt sich NICHT teilen (404): ihr Inhalt wechselt ständig |
| DELETE | `/api/worksheets/{id:int}/share` | Link abschalten = Widerruf; ein erneutes Teilen erzeugt ein **neues** Token, gedruckte QR-Codes laufen danach ins Leere |
| GET | `/api/worksheets/shared/{token}` | **`[AllowAnonymous]`** — das geteilte Blatt hinter dem Link: Name + Aufgaben (FEN, Ausrichtung, Überschrift, Begleittext, **Lösung**), keine Besitzer-Daten |

**Themen**: ein Blatt trägt bis zu 12 frei wählbare Themen (`Worksheet.Themes`, CSV) — wonach man ein
altes Blatt wiederfindet. Vorgeschlagen werden sie aus den AUFGABEN: beim Senden wandern die Themen des
Quell-Puzzles als `WorksheetItem.SourceThemes` mit (aus `BookPuzzles.Tags` bzw. `Puzzles.Themes`, roh wie
überall in der App — für diese Keys gibt es keine Übersetzung), der Editor zählt sie und bietet die
häufigsten zum Anklicken an; eigene Wörter („U12 Mittwoch") sind erlaubt. Die Themen wirken NUR in der
Übersicht (Anzeige + Filterzeile) — nicht im Druck und nicht hinter dem geteilten Link. Einzelne Aufgaben
tragen bewusst KEINE eigenen Tags (Überschrift + Begleittext reichen je Stellung).

**Teilen + durchspielen**: ein Blatt kann einen öffentlichen Link bekommen (`/w/{token}`, ohne Anmeldung
— wie `/g/` und `/l/`), der auf dem Ausdruck in der **Fußzeile jeder Seite als QR-Code** steht (ein
einzelnes Blatt auf dem Tisch soll allein funktionieren). Hinter dem Link wird das Blatt GELÖST: dafür
wandert beim Senden die Lösung der Quelllinie als `WorksheetItem.SolutionMoves` (UCI ab der
Aufgabenstellung, Gegnerzüge eingeschlossen) mit; auf dem Papier steht sie nie. Aufgaben ohne Lösung
(FEN von Hand, Stellung aus einer Kommentar-Variante) bleiben dort ein **Rechenbrett** — Züge frei,
nichts wird bewertet. Die Prüf-Logik steht als eigene Klasse `WorksheetTask` in
`worksheet-solve.component.ts` (ohne Angular/HTTP, damit die Regel einzeln testbar bleibt). **Achtung**:
wer den Link hat, sieht damit auch die Lösungen des Kurses — das ist die bewusste Entscheidung des
Teilenden, und Abschalten ist der Widerruf.

Frontend: `/worksheets` (Übersicht), `/worksheets/:id` (Bearbeiten: ziehen, Überschrift/Begleittext,
FEN von Hand, Teilen-Schalter mit Link + QR), `/worksheets/:id/print?print=1` (Druckansicht, öffnet den
Druckdialog), `/w/:token` (öffentliche Löseseite). Gesendet wird über
`<app-send-to-worksheet>` (Menüzeile mit Ziel-Untermenü bzw. `[asButton]` in Werkzeugleisten) — eingebaut im
Kurs-⋮, im Kapitel-⋮, in der Steuerleiste des Durchsehens und im ⋮ der Puzzle-Aktionszeile (neben
„♥ Letztes Puzzle"). Die PDF macht der Browser; gezeichnet wird mit `FlashcardBoardComponent` als
Inline-SVG (chessground-Figuren sind CSS-Hintergründe und werden nicht gedruckt).

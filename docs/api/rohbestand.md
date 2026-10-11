# Rohbestand (`LibraryGames`) — der Vorrat, aus dem die Punktepartie ausgewaehlt wird

> Ausgelagert aus `CLAUDE.md` am 2026-10-11 (Abschnitt „REST API → Rohbestand (`LibraryGames`) — der Vorrat, aus dem die Punktepartie ausgewaehlt wird“). Neue Doku zu diesem Bereich gehört HIERHER, nicht in die CLAUDE.md.


Eine PGN-Sammlung landet NICHT direkt als `GameAnalysis`. Eine Analyse ist ein Versprechen an die
Engine — je Halbzug ein Auftrag, bei Tiefe 20 grob zwanzig Sekunden je Stellung, also rund eine
halbe Stunde je Partie. Bei den 130 679 kommentierten Meisterpartien aus dem ChessBase-Magazin
waeren das Jahre Rechenzeit; die AUSWAHL ist damit die eigentliche Arbeit, und `LibraryGames` ist
der Ort, an dem die Partien liegen, waehrend sie getroffen wird.

Die Zahlenspalten sind der Sinn der Tabelle: sie erlauben das Sortieren, ohne 338 MB PGN erneut zu
lesen. Sie werden nach und nach befuellt, und ein noch nicht befuelltes Feld ist `null` und nicht 0
— „nicht gezaehlt" und „keine Kommentare" sind verschiedene Aussagen.

| Spalte | Was drinsteht |
|---|---|
| `SourceFile` / `SourceTitle` / `SourceRef` / `ExternalGameId` | Herkunft: Datei, Ausgabe (`CBM 104 Extra`), Quelle (`ChessBase`), deren Partie-Kennung |
| `MovesHash` | SHA-256 ueber die normalisierte Zugfolge — Bewertungszeichen und Zugnummern raus, sonst faende der Abgleich keine einzige Dublette |
| `DuplicateOfId` / `Status` | Dieselbe Partie von zwei Leuten kommentiert: behalten wird die mit den MEISTEN kommentierten Halbzuegen, die andere zeigt darauf. Geloescht wird nichts — die zweite Meinung kann die bessere sein |
| Kopfdaten | `White`/`Black`/`WhiteElo`/`BlackElo`/`Result`/`Event`/`Site`/`Round`/`PlayedOn`/`Eco`/`StartFen`/`PlyCount` |
| `Annotator` | Wer kommentiert hat (im Bestand traegt JEDE Partie einen) |
| `CommentCount` / `CommentedPlies` / `CommentChars` | Wie viele Kommentare, wie viele HALBZUEGE einen tragen, wie viel Text. Die mittlere Zahl entscheidet: die Punktepartie haelt an kommentierten Zuegen an, drei lange Absaetze am Schluss machen sie stumm |
| `NagCount` / `VariationCount` | Symbol-Bewertungen und Nebenvarianten — Zeichen ernsthafter Arbeit, aber der Spielende sieht sie nie |
| `Languages` | CSV von ISO-Kuerzeln (`de`, `en,de`), `und` = nicht bestimmbar. NIE `null` lassen, sobald geprueft — sonst holt der naechste Durchgang dieselben Zeilen wieder |
| `OpeningLine` | Die ersten 30 Halbzuege normalisiert („e4 e5 Nf3 …"), indiziert. Damit ist der Bestand eine EROEFFNUNGSSTATISTIK: „wie viele Partien spielen dieselben ersten k Zuege" ist eine Praefix-Suche statt einer Volltextsuche ueber 338 MB |
| `FirstCommentedPly` | Der erste kommentierte Halbzug — zusammen mit `OpeningLine` die Grundlage von `GuessStartPly` |
| `SearchText` | Spieler, Turnier und Kommentator kleingeschrieben in einer Spalte, mit VOLLTEXT-Index — das Feld, ueber das die Bestandssuche laeuft |
| `Score` | Eignungsnote 0–100 (`GuessSuitability`), eigene Spalte statt Formel in der Abfrage: die Gewichtung wird sich aendern |
| `Pgn` | Die Partie selbst (LONGTEXT). Bewusst hier und nicht als Verweis auf Datei und Byte-Position — die Quelldatei ist ein Fund im Ablage-Ordner und keine Zusage |

`LibraryGameReader` liest eine Partie in EINEM Textdurchgang und fasst dabei kein Brett an: das
Nachspielen (`GamePlies.Parse`) ist bei 130 000 Partien der Unterschied zwischen Minuten und
Stunden. Der Preis ist, dass dieser Durchgang die Zuege NICHT prueft — das tut erst die Uebernahme
in eine `GameAnalysis`, und dort gehoert es auch hin. Im Rohbestand zu liegen heisst „eingelesen",
nicht „gut".

**Stand 2026-09-10 (nur DEV, Prod bekommt die leere Tabelle mit dem naechsten Tag):** 130 572
Partien aus `kommentierteMeistergames.pgn` (338 MB, ChessBase-Magazin-Export). 72 Eintraege der
Datei trugen keinen einzigen Zug (Theorie-Fragmente mit `[White "?"]`) und fielen weg, 28 Zeilen
sind Dubletten. 94 898 Partien tragen mindestens einen kommentierten Halbzug. Sprache: 32 594
englisch, 11 031 deutsch, 8 676 franzoesisch, 1 509 gemischt en/de, der Rest verteilt; 58 370 mal
`und` — davon haben aber 54 161 weniger als 400 Zeichen Text, es ist also meist wirklich nichts da
(Quellenangaben, Seitenzahlen, blosse Symbole). Bei den Partien MIT ordentlich Text trifft die
Erkennung in neun von zehn Faellen.

Die Spitze der Note sieht aus, wie sie soll: Aronian–Ding mit 57 von 59 kommentierten Halbzuegen,
Giri ueber seinen eigenen Sieg gegen Carlsen, Tal–Timman von Kuljasevic. **Bekannte Unschaerfe:**
`CommentChars` zaehlt auch ChessBase-Markup im Kommentar mit (`[%cal …]`, `[%csl …]` — 54 841
Partien enthalten welches). Das sind echte Anmerkungen, nur eben Pfeile statt Prosa; auf die
Reihenfolge an der Spitze wirkt es sich nicht sichtbar aus.

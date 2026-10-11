# Computer-Linien + Pfeil für den besten Zug (0.521.0)

> Ausgelagert aus `src/frontend/CLAUDE.md` am 2026-10-11. Neue Doku zu diesem Bereich gehört HIERHER.


Seit 0.745.0 (Wunsch 2026-10-10) standardmäßig AN, ebenso die Ansicht „Beide" (Stockfish + Lc0), wo es eine zweite Analyse gibt:
gemerkt wird nur noch ein ausdrückliches Aus (`'0'` bzw. `'primary'`/`'alt'`). Im Fehler-Training bleiben Linien und Pfeil weg.

Zwei Schalter in der Kopfzeile des Rückblicks (`game-review.component.ts`), je Gerät gemerkt
(`rookhub_game_lines`, `rookhub_game_arrow`, localStorage): die KANDIDATEN der eigenen Partie-Analyse für die
Stellung auf dem Brett (bis zu fünf, Bewertung in Weiß-Sicht, Variante in SAN — `computer-lines.util.ts`,
`uciLineToSan` + `normalizeCastlingUci` aus features/analysis, höchstens `COMPUTER_LINE_PLIES` 10; der gespielte
Zug ist hinterlegt) und ein grüner Pfeil für den besten Zug. Keine Engine im Browser — nur `GET …/evals`.
- Der Pfeil geht als Ausgabe `arrowsChange` an die Seite, die ihn über `[arrows]` auf IHR Brett legt
  (`ChessBoardComponent.arrows` → Chessground `setAutoShapes`; selbst gezogene Rechtsklick-Pfeile bleiben).
- **Im Fehler-Training aus** (`[engineHidden]="!!training()"`): Linien und Pfeil verrieten die Lösung; auch die
  Schalter verschwinden.
- Die Endstellung und nicht gerechnete Stellungen haben keine Linien (die Analyse rechnet nur Stellungen, in
  denen noch gezogen wird). Analysen von vor 0.521.0 haben keine Varianten → je Kandidat nur der Zug.
- Beschriftung seit 0.522.0 mit eigenen Keys `games.review.lines` / `games.review.arrow` (vorher geliehen, weil die
  Sprachdateien beim Bau nicht lesbar waren).

**Buchzüge** (0.522.0): Klasse `book` (📖, `#a88865`, zwischen Good und Inaccuracy wie bei chess.com) für die Halbzüge
aus `GameEvals.bookPlies` — Züge aus den für die Erweiterung markierten Repertoires DES BETRACHTERS (Server rechnet,
anonym leer). Das Etikett schlägt jede andere Klasse; `base` und Genauigkeit bleiben (ein schwacher Repertoirezug kostet
weiter Genauigkeit), in der Kurve kein Punkt, im Fehler-Trainer keine Aufgabe. Erklärung am Abzeichen: `games.review.bookHint`.

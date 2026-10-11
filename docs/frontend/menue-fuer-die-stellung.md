# ⋮-Menü für die Stellung (0.527.0)

> Ausgelagert aus `src/frontend/CLAUDE.md` am 2026-10-11. Neue Doku zu diesem Bereich gehört HIERHER.


`features/analysis/position-menu.component.ts` — EIN Menü für Analysebrett (Zugleiste, neben Drehen/Zurücksetzen) und
Partieseite (Navigationsleiste, am Handy ganz rechts): auf Chessable suchen, Stellung teilen, FEN kopieren, dazu —
angemeldet und mit externer Engine — „Im Hintergrund analysieren" (`AnalysisJobDialogComponent`) und die Auftragsliste.
Die Adressen stehen rein in `position-links.util.ts` mit literalen Vektoren: Chessables FEN-Suche schreibt `/` als `U`
und Leerzeichen als `%20` (dieselbe Regel wie der „Search FEN"-Knopf der RepCheck-Erweiterung, ohne Kurs-Id), der
Teilen-Link ist `/analysis?fen=…&orientation=black` (öffentliche Route). Teilen nimmt am Handy (`pointer: coarse`) das
Teilen-Blatt des Geräts, sonst die Zwischenablage. Die Engine-Frage (`GET /api/engine/external`, fragt serverseitig
Lichess) stellt das Menü erst beim ersten ÖFFNEN — das Analysebrett kennt die Antwort und reicht sie über `engines`
herein. Auf der Partieseite ist die Stellung die auf dem Brett (`positionFen()`: Ende der Live-Nebenvariante, sonst
der Partiezug).

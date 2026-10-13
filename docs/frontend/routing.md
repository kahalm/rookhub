# Routing

> Ausgelagert aus `src/frontend/CLAUDE.md` am 2026-10-11. Neue Doku zu diesem Bereich gehört HIERHER.


| Route | Component | Auth |
|-------|-----------|------|
| `/login` | LoginComponent | nein |
| `/register` | RegisterComponent | nein |
| `/dashboard` | DashboardComponent | ja |
| `/profile` | ProfileComponent | ja |
| `/friends` | FriendsComponent | ja |
| `/repertoires` | RepertoireListComponent | `authGuard` (alle eingeloggten Nutzer; API ist pro-Nutzer abgesichert) |
| `/repertoires/:id` | RepertoireDetailComponent | `authGuard` (alle eingeloggten Nutzer) |
| `/tournaments` | TournamentListComponent | ja |
| `/tournaments/:id` | TournamentDetailComponent | ja |
| `/weekly` | WeeklyListComponent | `adminGuard` (vorerst nur Admin; Lese-API bleibt offen) |
| `/analysis/compare/:id` | MoveComparisonComponent („Züge vergleichen“: Kandidaten nach Stärke, je schwächerem die besten Antworten gegen den besten Zug + Begründung; fragt alle 3 s nach, solange gerechnet wird) | `authGuard` |
| `/analysis/jobs` | AnalysisJobsComponent (Hintergrund-Analyseaufträge: Liste + gespeicherte Linien + Tiefe/Linien anpassen; steht VOR `/analysis`) | `authGuard` |
| `/reconstruct` | ReconstructListComponent („Partie rekonstruieren": Liste anlegen/öffnen/löschen) | `authGuard` + `menuGuard('reconstruct')` |
| `/games/:id` | SharedGameComponent im Modus `own` (`data.mode`): eigene gespeicherte Partie als Seite — Brett, Zugliste, Bewertungskurve, Analysieren, Teilen-Link; seit 0.513.0 statt des PGN-Viewer-Dialogs | `authGuard` + `menuGuard('games')` |
| `/games/scoresheet` | ScoresheetUploadComponent (Partieformular einlesen: Foto + Notationssprache, fragt alle 3 s nach, bis die Partie da ist; die letzten Einlesungen darunter; Literal VOR `/games/:id`) | `authGuard` + `menuGuard('scoresheet')` |
| `/games/:id/edit` | GameEditComponent (Partie korrigieren: Kopfdaten, Cursor-Brett ersetzen/einfügen/löschen; bei eingelesenen Partien Foto, Formular-Einträge, Lesarten mit Reichweite, Rest wird nach jeder Änderung serverseitig neu aufbereitet — Logik rein in `game-edit.util.ts`) | `authGuard` + `menuGuard('games')` |
| `/reconstruct/:id` | ReconstructDetailComponent (Arbeitsplatz: Teile links, Brett rechts; Zugfolgen werden lokal mit chess.js mitgespielt, geprüft wird serverseitig) | `authGuard` + `menuGuard('reconstruct')` |
| `/analysis` | AnalysisComponent (seit 0.603.0 angemeldet mit Verlauf der letzten 20 Analysen und Stern-Markierungen, `?history=<id>` öffnet einen Eintrag) | nein (öffentlich; Stockfish-MultiPV-Analyse — lokal per WASM, eingeloggt wahlweise über eine externe Engine — direkt bei RookHub angemeldet oder über das eigene Lichess-Konto, siehe „Externe Engine" im Haupt-CLAUDE.md) |
| `/install` | InstallComponent | nein (öffentlich; APK-Download + PWA-Install, plattformabhängig via `PwaInstallService`) |
| `/stats` | StatsComponent | ja (Puzzle-Elo-Kurve + Stats; `GET /api/puzzles/elo-history`) |
| `/training-goals` | TrainingGoalsComponent | `authGuard` (Tagesziele setzen, Heute-Fortschritt + Ziele-Heatmap; `/api/training-goals/*`) |
| `/weekly/:weeklyId` | BookPuzzleComponent (Wochenpost-Modus) | `adminGuard` (vorerst nur Admin) |
| `/ebooks` | EbookListComponent (E-Book-Bibliothek, 0.764.0: Suche, Schlagwort-Chips, Sortierung, Karten mit Titelbild und Lesestand; Hochladen für jeden Angemeldeten, Sichtbarkeit je Buch über Gruppen) | `authGuard` + `menuGuard('ebooks')` |
| `/ebooks/:id` | EbookReaderComponent (PDF-Leser mit pdf.js aus `/assets/pdfjs/`, Seite x / N, Zoom, gemerkter Lesestand; andere Formate: Download-Karte) | `authGuard` + `menuGuard('ebooks')` |
| `/courses` | CourseListComponent | `courseAccessGuard` (Admin oder Gruppe mit Buch-Freigabe) |
| `/courses/:bookId` | CourseDetailComponent (Detailseite: Metadaten + Kapitel-Verwaltung, Stellungen per Memo einfügen) | `courseAccessGuard` |
| `/courses/:bookId/flashcards` | FlashcardsComponent (Lern-/Druckansicht: AUSGANGSSTELLUNG vorn [Aufgabe], Lösung+Abschlusstext hinten; `?lines=<ids>`/`?chapter=`; MUSS vor der `:mode`-Route stehen) | `courseAccessGuard` |
| `/repertoires/:id/flashcards` | FlashcardsComponent (UMGEKEHRT: Endstellung+[%cal]-Pfeile vorn, Linie+Abschlusstext hinten; `?lines=<lineKeys>`) | `authGuard` |
| `/courses/:bookId/calc` | CalculationComponent (Kalkulations-Modus; MUSS vor der `:mode`-Route stehen) | `coursePlayGuard` |
| `/courses/:bookId/:mode` | BookPuzzleComponent (Kursmodus) | `courseAccessGuard` |
| `/` | redirect -> `/dashboard` | - |
| `**` | redirect -> `/dashboard` | - |

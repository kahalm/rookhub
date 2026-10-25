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
| `/weekly` | WeeklyListComponent | `authGuard` + `menuGuard('weekly')` (Lese-API bleibt offen) |
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
| `/weekly/:weeklyId` | BookPuzzleComponent (Wochenpost-Modus) | kein Guard (öffentlich lesbar; noch nicht fällige Posts liefert die API nur mit `weeklyposts.manage`) |
| `/ebooks` | EbookListComponent (E-Book-Bibliothek, 0.764.0: Suche, Schlagwort-Chips, Sortierung, Karten mit Titelbild und Lesestand; Hochladen für jeden Angemeldeten, Sichtbarkeit je Buch über Gruppen) | `authGuard` + `menuGuard('ebooks')` |
| `/ebooks/:id` | EbookReaderComponent (PDF-Leser mit pdf.js aus `/assets/pdfjs/`, Seite x / N, Zoom, gemerkter Lesestand; andere Formate: Download-Karte) | `authGuard` + `menuGuard('ebooks')` |
| `/courses` | CourseListComponent | `authGuard` + `menuGuard('courses')` (`course-access.guard.ts` gibt es nicht mehr, Codereview F1-017; was lesbar ist, entscheidet die API) |
| `/courses/:bookId` | CourseDetailComponent (Detailseite: Metadaten + Kapitel-Verwaltung, Stellungen per Memo einfügen) | `authGuard` + `menuGuard('courses')` |
| `/courses/:bookId/flashcards` | FlashcardsComponent (Lern-/Druckansicht: AUSGANGSSTELLUNG vorn [Aufgabe], Lösung+Abschlusstext hinten; `?lines=<ids>`/`?chapter=`; MUSS vor der `:mode`-Route stehen) | `authGuard` + `menuGuard('courses')` |
| `/repertoires/:id/flashcards` | FlashcardsComponent (UMGEKEHRT: Endstellung+[%cal]-Pfeile vorn, Linie+Abschlusstext hinten; `?lines=<lineKeys>`) | `authGuard` |
| `/courses/:bookId/calc` | CalculationComponent (Kalkulations-Modus; MUSS vor der `:mode`-Route stehen) | `coursePlayGuard` |
| `/courses/:bookId/:mode` | BookPuzzleComponent (Kursmodus) | `coursePlayGuard` |
| `/admin` | AdminComponent (Tabs je Recht, Deep-Link `?tab=<key>`, Keys in `features/admin/admin-tabs.ts`) | `adminAreaGuard` (je Recht, F5-005 — siehe unten) |
| `/` | redirect -> `/dashboard`, Gäste -> `GUEST_START_URL` (nach einer Anmelde-Übernahme löst `HandoffService.leaveGuestStart` den Start neu auf, UX-025) | - |
| `**` | `shared/not-found/NotFoundComponent` (guard-frei, auch in LeagueHub; Codereview UX-026) | - |

## Admin-Bereich: `adminAreaGuard` (F5-005)

`/admin` hängt nicht am Admin-Flag, sondern an den Rechten: `features/admin/admin-area.guard.ts` lässt durch, wer
mindestens einen Admin-Tab sehen darf (`canOpenAdmin`/`visibleAdminTabs` aus `admin-tabs.ts`, Tabelle
`ADMIN_TAB_PERMISSIONS`; Admins dürfen alles). Ein Deep-Link `?tab=<key>` auf einen Tab ohne Recht leitet MIT Hinweis
(`blockedNotice`) auf `/admin?tab=<erster erlaubter>` um; ganz ohne Recht Hinweis + `/dashboard`, Gäste zur Anmeldung
mit Rücksprung (`loginRedirect`). Damit funktionieren die Glockenlinks (`/admin?tab=messages&thread=…`,
`/admin?tab=uploads`, `/admin`) für Rechteinhaber ohne Admin-Flag. Der Menüeintrag „Admin" (`NavbarComponent.showAdmin`)
nutzt dieselbe Funktion. Die Admin-Seite rendert nur die erlaubten Tabs; die mat-tab-Indizes zählen daher über
`visibleTabs()`, Auswahl und URL gehen immer über den Key (`adminTabIndex(key, tabs)`), nie über einen festen Index.
`core/admin.guard.ts` (`adminGuard`, nur Admin-Flag) bleibt für die Turnier-App.

Aufbau der Seite (Zerlegung Z30, 2026-10-25): `AdminComponent` hält nur Kopf, Tab-Leiste, Deep-Link und die behaltenen
Tabs (OnPush). Jeder Tab ist eine eigene Komponente in `features/admin/tabs/` — Nutzer, Bücher und Gruppen als
`admin-users-tab`, `admin-books-tab` (Filter rein in `admin-book-filter.ts`) und `admin-groups-tab`. Diese drei hängen
OHNE `matTabContent` im `mat-tab` und laden daher wie vorher beim Seitenaufruf, soweit der Tab sichtbar ist; die
übrigen bleiben lazy bzw. (Menü, Rollen) nach dem ersten Öffnen behalten (F5-020). Die Gruppenliste, die Gruppen-,
Bücher-, Menü- und Rollen-Tab teilen, liegt in `admin-groups.store.ts` (je Seite bereitgestellt): mit `groups.manage`
die volle Liste, sonst nur die Auswahl (`groups/options`).

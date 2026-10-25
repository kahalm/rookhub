# Formular-Korrektur als geteilte Sitzung (0.573.0)

> Ausgelagert aus `src/frontend/CLAUDE.md` am 2026-10-11. Neue Doku zu diesem Bereich gehört HIERHER.


`features/games/sheet-edit-session.ts` (`SheetEditSession`) hält den Arbeitsstand einer Partie-Korrektur in Signalen:
Halbzüge mit Formular-Einträgen und Lesarten, Cursor, Ersetzen/Einfügen/Löschen/Bestätigen, Ausschnitt des Fotos und
das Neu-Aufbereiten des Rests nach jeder Änderung (die Regeln selbst stehen rein in `game-edit.util.ts`). Benutzt von
RookHubs `GameEditComponent` (`/games/:id/edit`, die Felder dort sind Aliase auf die Sitzung) UND der
Formular-Korrektur der Vereins-Datenbank in LeagueHub (`src-leaguehub/app/features/club/club-scan-page.component.ts`; Kopfdaten
in `club-side-form.component.ts`, Ausgabe in `scan-export.ts`, Zusammenführen in `scan-merge.ts` — Zerlegung Z31).
Die Seite gibt nur mit, woher der Rest kommt (`resolve`), und hängt das Abonnement an ihre Lebensdauer (`bind`). Wer an
der Korrektur etwas ändert, ändert es hier — sonst laufen die beiden Seiten auseinander.

Seit 0.646.0 trägt ein Halbzug den Befund der Engine-Prüfung beim Einlesen (`EditPly.check`/`ScoresheetPly.check`:
`replaced` | `suggested`, Server: „Engine-Prüfung der Lesung" im Haupt-CLAUDE.md). Beide Seiten zeigen ihn als Chip neben
„unsicher" („von der Engine korrigiert" / „Engine zweifelt", Erklärung im `title`), solange der Zug nicht bestätigt ist;
ein vom Nutzer gesetzter Zug (`userPly`) hat keinen. `fromServer`/`toServer` reichen ihn durch.

**LeagueHub für mehrere Vereine (0.699.0, Server 0.698.0) — `src-leaguehub/app/core/club-context.service.ts`.** Der Server kennt den
Verein einer Anfrage über `?club=` (Haupt-CLAUDE.md „LeagueHub — Vereine als Mandanten"). `ClubContextService` holt die
Vereine des Kontos EINMAL je Konto (`GET /api/league/me` → `{ clubs, current }`), wählt gemerkt (localStorage `lh-club`) vor
Server-Vorgabe vor dem ersten (`pickClub`) und stellt `club()`, `anonName()` (Ersatzname anonymisierter Spieler, früher fest
`ANON_NAME = 'Schwaz'` — die Konstante ist weg) und `clubName()` als Signale bereit. `leagueClubInterceptor` (nur in
`leaguehub`, über `provideRhHttpClient([leagueClubInterceptor])`) wartet bei jedem `/api/league/…`-Aufruf außer `/me` und
`/s/…` auf `ensure()` und hängt `club=<id>` an — die Dienste (`LeagueApiService`, `ClubApiService`) bleiben unverändert.
Mehrere Vereine: Umschalter im Kopf (`app.component`), der Wechsel merkt sich den Verein und lädt die Seite neu. Teilen-Seiten
(`/s/:token…`) nehmen den Verein des LINKS: `share-page` aus der Antwort (`useShareClub`), Hochladen/Formular holen ihn mit
`useShare(token)`; `shareClub` schlägt den angemeldeten. Texte, die früher „SK Schwaz"/„Schwaz" sagten, lesen jetzt
`clubName()`/`anonName()` (Zugangs-Sperrkarten sprechen allgemein von den Vereinsgruppen der teilnehmenden Vereine);
`importSummary(r, anon)` bekommt den Namen mit. Startseite: Vorauswahl der Mannschaft über `ownsTeam(teamPrefix, team)` (SPIEGEL
von `LeagueClub.OwnsTeam`). Ligaauswahl (0.710.0): nur Ligen mit eigener Mannschaft (Server filtert); Verwalter haben den
Schalter „alle Ligen der Region" (`lh-all-leagues` im localStorage, Vorgabe aus → `index(true)` = `?all=true`); eine gemerkte
Liga (`leaguehub`/`?liga=`) außerhalb der Liste fällt auf die erste eigene; ohne eigene Liga (`filtered && total > 0`) die Karte
„Noch keine Liga mit einer Mannschaft von <Verein>" (Verwalter: „Alle Ligen zeigen"). Datenschutz: `LEGAL_SITE.leagueClub` nennt den Verein (`legal.privacy.leagueClub`). Specs:
`provideTestClub()` aus `core/club-context.testing.ts` (Verein „SK Testdorf"/„Testdorf" — bewusst nicht Schwaz).
`reload()` (0.700.0) verwirft die gemerkte `/me`-Antwort und fragt neu (gewählter Verein bleibt, solange das Konto dazugehört) —
die Vereinsverwaltung `/vereine` (`features/clubs/clubs-page.component.ts`, Admin + `league.manage`) ruft es nach Anlegen,
Ändern, Zuordnen und Lösen, damit der Umschalter im Kopf den neuen Verein kennt. Deren 390-px-Spec misst wie
`touch-targets.spec` in einem iframe mit den globalen Styles (die Handy-Regeln greifen im 1400-px-Karma-Browser sonst nie).
**Region statt Quelle (0.704.0):** `LeagueClubInfo.region`/`AdminClub.region`/`ClubInput.region` (`tirol` | `bayern`) ersetzen
`source`. `/vereine` hat die Auswahl „Region" (`clubRegionText`: „Tirol (chess-results)", „Bayern (Ligamanager + Schachkreis
Zugspitze)", Rückfrage beim Umstellen, Absage `invalidRegion`); Kopf-Text (`app.component.region`) und Fußzeile der Startseite
(„Quelle: … Ligamanager … und vom Schachkreis Zugspitze") fragen `region === 'bayern'`.

LeagueHub spricht die Vereins-Datenbank über `ClubApiService.client(share)`: ohne `share` angemeldet
(`/api/league/club`), mit dem Token eines Teilen-Links ohne Konto (`/api/league/s/{token}/club`). Einlesungen heißen dort
`ref` — angemeldet die Nummer, ohne Konto der geheime Schlüssel. Die Übersicht vor dem PGN-Import rechnet
`features/club/import-review.ts` (rein, Spiegel der Server-Regel `ClubSideResolver.Build`).

## Formular-Werkbank: auch die Oberfläche ist geteilt (0.816.0)

Seit 0.816.0 teilen RookHub und LeagueHub nicht nur die Sitzung, sondern die ganze Oberfläche der Korrektur:
`features/games/sheet-workbench/` (Wunsch 2026-10-21: „die beiden sollten ident sein und nicht doppelt programmiert",
Aufteilung wie in LeagueHub).

- `SheetWorkbenchComponent` (`app-sheet-workbench`): Foto | Brett | Prüfen + Zugliste, Tasten ←/→, Folgen der Seite.
  Eingaben: `session` (die `SheetEditSession` der Seite), `photos` (`ScanPhotos`), `flipped` (zweiseitig), `evals` +
  `savedSans` (Analyse der GESPEICHERTEN Partie → Bewertungskurve, `correctionCurve`), `boardTheme`/`pieceSet`.
  Inhaltsplätze: `[sheetPhotoTools]` (RookHub: Foto öffnen/herunterladen), `[sheetPlyExtra]` (RookHub: Kommentar zum Zug),
  `[sheetEnd]` (LeagueHub: „Weiter zu den Namen").
- `ScanPhotos` (vorher LeagueHub): Seiten, weitere Fotos je Seite (`fetchView`), Objekt-Adressen und — neu — die Datei je
  Seite (`blob`, für RookHubs Öffnen/Herunterladen). RookHub hat keine weiteren Fotos; `fetchView` lehnt dort ab.
- `SheetEngineComponent` (`app-sheet-engine`, vorher `lh-scan-engine`): zuschaltbare Engine, gemerkt unter dem alten
  Schlüssel `lh-scan-engine`.
- Texte: `sheetEdit.*` (en/de/hr/hu); die alten `games.edit.*`-Texte der Korrektur sind entfernt. LeagueHub zeigt sie in der
  Sprache der Oberfläche (wie die geteilte Spielerkarte).
- Stile: `sheet-workbench.component.scss` — LeagueHub-Gestaltung mit Rückfall auf die RookHub-Tokens (`--sw-*`). Die
  Breitbild-Regel ≥ 2 400 px von LeagueHub (`.wrap.wide .scan-layout …`) bleibt global in `leaguehub.scss` und greift weiter.
- RookHub verliert dabei den Partie-Rückblick (Kurve + gespeicherte Computer-Linien) auf der Korrekturseite; dafür die
  Kurve der Werkbank (Zickzack = falsch gelesener Zug) und die Live-Engine.


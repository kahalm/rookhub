// Routen-Katalog aller fünf Oberflächen.
//
// Jede Zeile: { app, url, auth, label?, main?, foldExempt? }
//   url  — Pfad; {name} wird durch einen Parameter aus seed.mjs ersetzt. Fehlt der Parameter, wird die Route
//          übersprungen und im Bericht als „nicht auflösbar" vermerkt (statt eine 404 zu fotografieren).
//   auth — 'any'  = abgemeldet UND angemeldet rendern
//          'user' = nur angemeldet (abgemeldet landet man ohnehin auf der Anmeldung)
//          'anon' = nur abgemeldet (Anmeldemasken schicken Angemeldete weg)
//
// Neue Route in einer App → hier eintragen. Prüfung: `node sweep.mjs --list` zeigt den Katalog samt
// aufgelösten Adressen, ohne einen Browser zu starten.

export const APPS = {
  rookhub: { host: 'rookhub' },
  turnier: { host: 'turnier' },
  kidhub: { host: 'kidhub' },
  leaguehub: { host: 'leaguehub' },
  clubhub: { host: 'clubhub' },
};

// Viertes Feld: { label?, main?, foldExempt? } (ein bloßer Text gilt als label).
//   main       — CSS-Selektor der HAUPTSACHE (DESIGN.md 4.6, Regel 1) oder Liste; der erste mit sichtbarem Treffer gilt.
//                Listen: der erste Eintrag (querySelector liefert den ersten); dahinter Rückfälle für Leer-, Fehler-
//                und Sperrzustand, damit in jedem Zustand etwas trifft. null = bewusst keine Hauptsache.
//                Trifft keiner → Hinweis „Hauptsache nicht gefunden" (Katalog nachziehen, kein Fehler der Seite).
//   foldExempt — Ausnahme von der Falz-Prüfung (Vollbild-Brett, Dialog, Druckansicht); gemessen + fotografiert wird trotzdem.
//   expectedFailures — Fehlantworten der API, die die Seite ABSICHTLICH bekommt (eine Rückfrage „gibt es das schon?",
//                die mit 404 „nein" sagt). Liste von RegExps auf die Berichtszeile `METHODE /pfad?abfrage → STATUS`
//                (wie in `failedRequests`); Treffer zählen nicht als Befund, sie stehen unter `expectedFailed`. Eng
//                fassen (Methode, Pfad UND Status) und im Kommentar sagen, wo im Frontend die Absicht steht.
//                Zustände (states.mjs) erben die Liste ihrer Route.
const r = (app, url, auth = 'any', o = {}) => {
  const { label, main = null, foldExempt = false, expectedFailures = [] } = typeof o === 'string' ? { label: o } : o;
  return { app, url, auth, label, main, foldExempt, expectedFailures };
};

/** Ist diese Fehlantwort (Zeile wie in `failedRequests`, „GET /api/x/1 → 404") für die Route erwartet? */
export function isExpectedFailure(route, line) {
  return (route?.expectedFailures ?? []).some(re => re.test(line));
}

// Wiederkehrende Hauptsachen.
const PUZZLE_BOARD = ['.board-section app-puzzle-board', '.puzzle-page app-puzzle-board', '.revenge-done-card'];
// Partie-Ansicht (/games/:id, /club-games/:id, /g/:token, /r/:token): Brett, sonst „nicht gefunden"-Karte bzw. Ladekreis.
const GAME_BOARD = ['.shared-page .board-wrap app-chess-board', '.shared-page mat-card.empty-state', '.shared-page .center'];
// Anmeldung/Registrierung/Kennwort und Rechtstexte sind in allen fünf Oberflächen dieselben Komponenten
// (src/app/features/auth, …/legal, in den anderen Apps über @rh/*).
const AUTH_FORM = ['form.auth-form', '.auth-container p.auth-info', '.auth-container'];
// Reine Textseiten: Hauptsache = erster Inhaltsblock der Karte (Stand-Zeile, Überschrift „Kontakt" bzw. erster Absatz).
const LEGAL = ['.legal-container mat-card-content > :first-child', '.legal-container h1', '.legal-container mat-card'];
const LH_SCAN = ['.scan-board cg-board', '.scan-board app-chess-board', 'section.club-intro', 'section.gate'];
const LH_UPLOAD = ['section.panel', '.seg.club-kind', 'section.gate'];
const LH_FIXTURE = ['lh-fixture article.fixture', 'lh-fixture .empty', 'section.gate', 'section.empty', 'section.cta'];
const KID_PUZZLE = ['kid-puzzle cg-board', 'kid-puzzle app-puzzle-board', 'p.info', 'kid-error'];
const KID_STARS = ['kid-stars-play cg-board', 'app-puzzle-board', 'p.info'];
const COURSE_BROWSE = ['.browse-container .board-pane app-puzzle-board', '.browse-container .line-list .line-row', '.browse-container .empty-state', '.browse-container app-loading-spinner'];

export const ROUTES = [
  // ---------------------------------------------------------------- RookHub
  // Start: angemeldet → /dashboard, abgemeldet → /puzzles (GUEST_START_URL) — Selektoren für beide.
  r('rookhub', '/', 'any', { label: 'start', main: ['.dashboard-grid > mat-card', '.board-section app-puzzle-board'] }),
  r('rookhub', '/login', 'anon', { main: AUTH_FORM, expectedFailures: [/^POST \/api\/auth\/login → 401$/] }),
  r('rookhub', '/register', 'anon', { main: AUTH_FORM }),
  r('rookhub', '/forgot-password', 'anon', { main: AUTH_FORM }),
  r('rookhub', '/reset-password?token=ui-sweep', 'anon', { main: AUTH_FORM }),
  r('rookhub', '/dashboard', 'user', { main: ['.dashboard-grid > mat-card', '.dashboard-grid'] }),
  r('rookhub', '/profile', 'user', { main: ['app-theme-card .theme-section', '.profile-form', '.profile-container .retry-btn', '.profile-container mat-card'] }),
  r('rookhub', '/friends', 'user', { main: ['.friends-tabs mat-list-item', '.friends-tabs .empty-state'] }),
  r('rookhub', '/friends/{friendId}/stats', 'user', { main: ['.compare-card .compare-table tbody tr', '.compare-card'] }),
  r('rookhub', '/friends/{friendId}/revenge', 'user', { main: ['.rev-item', '.rev-container .empty-state'] }),
  r('rookhub', '/repertoires', 'user', { main: ['.repertoire-grid .rep-card', '.repertoire-container .empty-state'] }),
  r('rookhub', '/repertoires/{repertoireId}', 'user', { main: ['.viewer-layout app-chess-board', '.edit-panel app-repertoire-edit', '.detail-header .train-btn'] }),
  r('rookhub', '/repertoires/{repertoireId}/train', 'user', { main: ['.play app-puzzle-board', 'mat-card.msg'] }),
  r('rookhub', '/repertoires/{repertoireId}/flashcards', 'user', { main: ['.fc-study-card', '.fc-sheet .fc-card', '.fc-center'] }),
  // Repertoire in allen Ansichten: Baum, Lochfinder, Bearbeiten, Lernmodus — dazu je ein Schwarz- und ein Endspiel-Repertoire.
  r('rookhub', '/repertoires/{repertoireId}?mode=tree', 'user', { label: 'repertoires-tree', main: ['.viewer-layout', '.viewer-layout app-repertoire-tree', '.viewer-layout app-chess-board'] }),
  r('rookhub', '/repertoires/{repertoireId}?mode=holes', 'user', { label: 'repertoires-holes', main: ['.viewer-layout app-repertoire-holes', '.viewer-layout app-chess-board'] }),
  r('rookhub', '/repertoires/{repertoireId}?mode=edit', 'user', { label: 'repertoires-edit', main: ['.edit-panel app-repertoire-edit', '.detail-header .train-btn'] }),
  r('rookhub', '/repertoires/{repertoireId}/train?mode=learn', 'user', { label: 'repertoires-train-learn', main: ['.play app-puzzle-board', 'mat-card.msg'] }),
  r('rookhub', '/repertoires/{repertoireBlackId}', 'user', { label: 'repertoires-black', main: ['.viewer-layout app-chess-board', '.detail-header .train-btn'] }),
  r('rookhub', '/repertoires/{repertoireBlackId}/train', 'user', { label: 'repertoires-black-train', main: ['.play app-puzzle-board', 'mat-card.msg'] }),
  r('rookhub', '/repertoires/{repertoireEndgameId}', 'user', { label: 'repertoires-endgame', main: ['.viewer-layout app-chess-board', '.detail-header .train-btn'] }),
  r('rookhub', '/repertoires/{repertoireEndgameId}/train', 'user', { label: 'repertoires-endgame-train', main: ['.play app-puzzle-board', 'mat-card.msg'] }),
  r('rookhub', '/l/{lineToken}', 'any', { main: ['.viewer app-chess-board', '.shared-page .empty-state'] }),
  r('rookhub', '/puzzles', 'any', { main: PUZZLE_BOARD }),
  r('rookhub', '/puzzles/{puzzleId}', 'any', { main: PUZZLE_BOARD }),
  r('rookhub', '/puzzles/endless', 'any', { main: ['.play-screen app-puzzle-board', '.config-card .start-btn-top', '.config-screen'] }),
  r('rookhub', '/puzzles/endless/history', 'user', { main: ['.history-table tr.mat-mdc-row', '.mobile-only .session-card', '.history-container .empty-state'] }),
  r('rookhub', '/puzzles/book/{bookPuzzleId}', 'any', { main: PUZZLE_BOARD }),
  r('rookhub', '/puzzles/daily/{today}', 'any', { main: PUZZLE_BOARD }),
  r('rookhub', '/favorites', 'user', { main: ['.list mat-card.fav', '.fav-page mat-card.empty-state', '.fav-page app-load-error'] }),
  r('rookhub', '/worksheets', 'user', { main: ['.wl-list .wl-sheet', '.wl-empty', '.wl-clip'] }),
  r('rookhub', '/worksheets/{worksheetId}', 'user', { main: ['.wd-list .wd-item', '.wd-empty', '.wd-page .wd-hint'] }),
  // Druckansicht des Aufgabenblatts: fürs Papier gebaut — Ausnahme (DESIGN.md 4.6).
  r('rookhub', '/worksheets/{worksheetId}/print', 'user', { main: ['.ws-sheet .ws-task', '.ws-sheet', '.ws-empty'], foldExempt: true }),
  // Seit 0.778.0 auch ohne Anmeldung (für alle freigegebene Bücher + eigene Uploads).
  r('rookhub', '/ebooks', 'any', { main: ['.eb-grid .eb-card', '.eb-empty', '.eb-page app-load-error'] }),
  // Leser: Werkzeugleiste + Seiten in der Resthöhe — Sonderlayout wie das Brett (DESIGN.md 4.5).
  // Beide Leser-Routen für alle Rollen: seed.mjs gibt die Bücher für „Everyone" frei (abgemeldet sieht man genau diese).
  r('rookhub', '/ebooks/{ebookId}', 'any', { main: ['.rd-viewer', '.rd-download', '.rd-page app-load-error'], foldExempt: true }),
  // Leser mit erkannten Diagrammen: Buch links, Brett rechts (0.766.0 ff.) — ein Buch, das das Konto sehen darf.
  r('rookhub', '/ebooks/{ebookDiagramId}', 'any', { label: 'ebooks-diagramme', main: ['.rd-panel app-chess-board', '.rd-viewer', '.rd-page app-load-error'], foldExempt: true }),
  r('rookhub', '/weekly', 'user', { main: ['.wp-list .wp-card', '.weekly-container .empty-state', '.upload-card'] }),
  r('rookhub', '/weekly/{weeklyId}', 'user', { main: PUZZLE_BOARD }),
  r('rookhub', '/guess', 'any', { main: ['.game-row', '.lists .start-card', '.gl-container .start-card'] }),
  r('rookhub', '/guess/{guessId}', 'any', { main: ['.board-col app-chess-board', '.gb-container mat-card'] }),
  r('rookhub', '/analysis', 'any', { main: ['.board-wrap app-analysis-board', '.board-col app-position-setup.editor-full'] }),
  r('rookhub', '/analysis/games', 'user', { main: ['mat-card.ga', '.ga-container .empty-state', '.new-card'] }),
  r('rookhub', '/analysis/games/{analysisId}', 'user', { main: ['.board-col app-chess-board', '.gad-container mat-card'] }),
  r('rookhub', '/analysis/jobs', 'user', { main: ['mat-card.job', '.jobs-container .empty-state'] }),
  r('rookhub', '/analysis/compare/{comparisonId}', 'user', { main: ['.board-col app-chess-board', '.cmp-page mat-card'] }),
  r('rookhub', '/prep', 'user', { main: ['ul.hits > li', '.prep-search .hint', '.prep-search .q'] }),
  r('rookhub', '/prep/{prepId}', 'user', { main: ['.prep-card-scope lh-player-card', '.prep-card-scope'] }),
  r('rookhub', '/games', 'user', { main: ['.games-page .row.game', '.games-page mat-card.empty-state', '.games-page .center'] }),
  r('rookhub', '/games/mistakes', 'user', { main: ['.page .board-wrap app-chess-board', '.page button.start', '.page .empty-state', '.page > p.hint'] }),
  r('rookhub', '/games/scoresheet', 'user', { main: ['.sheet-page mat-card.upload', '.sheet-page mat-card.notice', '.sheet-page .center'] }),
  r('rookhub', '/games/{gameId}', 'user', { main: GAME_BOARD, expectedFailures: [/^GET \/api\/games\/\d+\/mistakes → 404$/] }),
  r('rookhub', '/games/{gameId}/edit', 'user', { main: ['.edit-page .board-card app-chess-board', '.edit-page mat-card.empty-state', '.edit-page .center'] }),
  r('rookhub', '/games/{analyzedGameId}', 'user', { label: 'games-analyzed', main: GAME_BOARD, expectedFailures: [/^GET \/api\/games\/\d+\/mistakes → 404$/] }),
  r('rookhub', '/club-games/{clubGameId}', 'user', { main: GAME_BOARD, expectedFailures: [/^GET \/api\/league\/club\/games\/\d+ → 403$/] }),
  // Vereinspartie über den LeagueHub-Teilen-Link (0.838.3, „Analyse“ ohne Konto)
  r('rookhub', '/s/{leagueShareGameToken}/club-games/{shareClubGameId}', 'any', { main: GAME_BOARD }),
  r('rookhub', '/g/{gameShareToken}', 'any', { main: GAME_BOARD, expectedFailures: [/^GET \/api\/games\/\d+\/mistakes → 404$/] }),
  r('rookhub', '/g/{analyzedShareToken}', 'any', { label: 'g-analyzed', main: GAME_BOARD, expectedFailures: [/^GET \/api\/games\/\d+\/mistakes → 404$/] }),
  r('rookhub', '/reconstruct', 'user', { main: ['.page mat-card.item', '.page .empty-state', '.page mat-card.new-card'] }),
  r('rookhub', '/reconstruct/{reconstructionId}', 'user', { main: ['.parts mat-card.part', '.parts .add-row', '.page > p.muted'] }),
  r('rookhub', '/r/{reconstructionToken}', 'any', { main: GAME_BOARD }),
  r('rookhub', '/w/{worksheetToken}', 'any', { main: ['.wsv-board app-puzzle-board', 'mat-card.wsv-done', 'mat-card.wsv-empty', '.wsv-center'] }),
  r('rookhub', '/remembered', 'user', { main: ['.remembered-page mat-card.item', '.remembered-page mat-card.empty-state', '.remembered-page .center'] }),
  r('rookhub', '/stats', 'user', { main: ['.stats-container .cards mat-card.stat', '.stats-container app-load-error', '.stats-container app-loading-spinner'] }),
  r('rookhub', '/leaderboards', 'user', { main: ['.lb-page .lb-row', '.lb-page .lb-card .empty-state', '.lb-page .lb-error', '.lb-page app-loading-spinner'] }),
  r('rookhub', '/training-goals', 'user', { main: ['.tg-container .tg-tabs .tab-body mat-card', '.tg-container app-load-error', '.tg-container app-loading-spinner'] }),
  r('rookhub', '/notifications', 'user', { main: ['.notif-container .list button.row', '.notif-container .empty-state', '.notif-container app-load-error'] }),
  r('rookhub', '/messages', 'user', { main: ['.msg-container .thread .bubble-row', '.msg-container .empty-state', '.msg-container mat-card.admin-hint', '.msg-container .reply', '.msg-container app-load-error'] }),
  r('rookhub', '/courses', 'user', { main: ['.courses-container .course-grid mat-card.course-card', '.courses-container .empty-state', '.courses-container app-loading-spinner'] }),
  r('rookhub', '/woodpecker', 'user', { main: ['.plans .plan', '.page .empty-state', '.page app-load-error'] }),
  r('rookhub', '/woodpecker/{woodpeckerPlanId}/solve', 'user', { main: ['.solver app-puzzle-board', '.page mat-card.done', '.page app-load-error'], expectedFailures: [/^GET \/api\/woodpecker\/\d+\/next → 404$/] }),
  r('rookhub', '/catalog', 'user', { main: ['.catalog .item-list .item-row', '.catalog mat-card:not(.admin-card) .empty-state', '.catalog mat-card:not(.admin-card) app-load-error', '.catalog mat-card:not(.admin-card) p.empty'] }),
  r('rookhub', '/courses/{bookId}', 'user', { main: ['.detail-page .head-card .actions button', '.detail-page .head-card', '.detail-page .center', '.detail-page > mat-card'] }),
  r('rookhub', '/courses/{bookId}/browse', 'user', { main: COURSE_BROWSE }),
  r('rookhub', '/courses/{bookId}/flashcards', 'user', { main: ['.fc-study .fc-study-card', '.fc-sheets:not(.fc-sheets--screen-hidden) .fc-card', '.fc-page .fc-center'] }),
  r('rookhub', '/courses/{bookId}/sequential', 'user', { main: PUZZLE_BOARD }),
  r('rookhub', '/courses/{bookId}/chapter/{chapterIndex}/browse', 'user', { main: COURSE_BROWSE }),
  r('rookhub', '/courses/{bookId}/chapter/{chapterIndex}/sequential', 'user', { main: PUZZLE_BOARD }),
  // Kurs in allen Modi: Zufall (ganzes Buch + Kapitel), nur markierte Karteikarten, Detailseite des eigenen Kalkulationskurses.
  r('rookhub', '/courses/{bookId}/random', 'user', { label: 'courses-random', main: PUZZLE_BOARD }),
  r('rookhub', '/courses/{bookId}/chapter/{chapterIndex}/random', 'user', { label: 'courses-chapter-random', main: PUZZLE_BOARD }),
  r('rookhub', '/courses/{bookId}/flashcards?marked=1', 'user', { label: 'courses-flashcards-marked', main: ['.fc-study .fc-study-card', '.fc-sheets:not(.fc-sheets--screen-hidden) .fc-card', '.fc-page .fc-center', '.fc-page .empty-state'] }),
  r('rookhub', '/courses/{calcBookId}/calc', 'user', { main: ['.calc-board-wrap app-puzzle-board', '.calc-page .calc-loading', '.calc-page > mat-card'] }),
  r('rookhub', '/courses/{studyBookId}', 'user', { label: 'courses-study', main: ['.detail-page .head-card .actions button', '.detail-page .head-card', '.detail-page .center', '.detail-page > mat-card'] }),
  r('rookhub', '/courses/{studyBookId}/browse', 'user', { label: 'courses-study-browse', main: COURSE_BROWSE }),
  r('rookhub', '/courses/{calcBookId}', 'user', { label: 'courses-calc-detail', main: ['.detail-page .head-card .actions button', '.detail-page .head-card', '.detail-page .center', '.detail-page > mat-card'] }),
  // Kurz-URL öffentlicher Kurse (/{slug}) leitet weiter; ein unbekannter einteiliger Alias zeigt „Seite nicht gefunden" (zweiteilig: not-found unten).
  r('rookhub', '/{courseSlug}', 'any', { label: 'course-slug', main: PUZZLE_BOARD.concat(['.detail-page .head-card', '.browse-container']) }),
  // Kapitel-Kurzlink /{slug}/{kapitel} (z. B. /noel/KW46): erstes Kapitel mit Aufgaben (Name aus seed.mjs).
  r('rookhub', '/{courseSlug}/{courseSlugChapter}', 'any', { label: 'course-slug-chapter', main: PUZZLE_BOARD.concat(['.detail-page .head-card', '.browse-container']) }),
  r('rookhub', '/ui-sweep-gibt-es-nicht', 'any', { label: 'slug-not-found', main: ['section.not-found .nf-actions', 'section.not-found'], expectedFailures: [/^GET \/api\/courses\/by-slug\/[^ ]+ → 404$/] }),
  r('rookhub', '/chessable', 'user', { main: ['.extension-note .store-links', '.extension-note'] }),
  r('rookhub', '/admin', 'user', { main: ['.admin-container table.users-table tr.mat-mdc-row', '.admin-container .users-table-wrap', '.admin-container mat-tab-group'] }),
  r('rookhub', '/help', 'any', { main: ['.help-container mat-card.help-section', '.help-container .help-toc'] }),
  r('rookhub', '/install', 'any', { main: ['.install-container mat-card.pwa-section', '.install-container mat-card.install-section'] }),
  // Rechtstexte: Hauptsache = erster Inhaltsblock.
  r('rookhub', '/privacy', 'any', { main: LEGAL }),
  r('rookhub', '/impressum', 'any', { main: LEGAL }),
  r('rookhub', '/account-deletion', 'any', { main: LEGAL }),
  r('rookhub', '/tournaments', 'any', { label: 'tournaments-moved', main: ['.moved > p', '.moved'] }),
  // Alte Turnier-Links (vor der eigenen Turnierseite) — dieselbe Umzugsseite, mit Weiterleitung auf turnier.*.
  r('rookhub', '/tournaments/{tournamentId}', 'any', { label: 'tournaments-moved-id', main: ['.moved > p', '.moved'] }),
  r('rookhub', '/t/{tournamentId}', 'any', { label: 'tournaments-moved-t', main: ['.moved > p', '.moved'] }),
  r('rookhub', '/diese-seite-gibt-es-nicht/xyz', 'any', { label: 'not-found', main: ['section.not-found .nf-actions a', 'section.not-found'], expectedFailures: [/^GET \/api\/courses\/by-slug\/[^ ]+ → 404$/] }),

  // ---------------------------------------------------------------- Turnierseite
  r('turnier', '/tournaments/calendar', 'any', { main: ['mat-card.entry-card', 'p.empty-state', 'app-loading-spinner'] }),
  // 404 auf /api/tournaments/{chessResultsId} = Turnier noch nicht importiert (tournament-directory-detail.component.ts,
  // lookupImported fängt es ab und zeigt dann „Importieren").
  r('turnier', '/tournaments/calendar/{directoryId}', 'any', { main: ['mat-card.detail-card', 'mat-card.load-failed'],
    expectedFailures: [/^GET \/api\/tournaments\/\d+ → 404$/] }),
  r('turnier', '/tournaments/{tournamentId}', 'any', { main: ['app-tournament-tables tr.mat-mdc-row', 'app-tournament-tables .player-card', 'app-tournament-tables p.empty-state', 'mat-card.load-failed'] }),
  r('turnier', '/t/{tournamentId}', 'any', { main: ['app-tournament-tables tr.mat-mdc-row', 'app-tournament-tables .player-card', 'app-tournament-tables p.empty-state', '.detail-container mat-card'] }),
  r('turnier', '/tournaments', 'user', { main: ['.rows mat-card.row', 'mat-card.card.empty-state', 'mat-card.card.failed'] }),
  r('turnier', '/tournaments/history', 'user', { main: ['.rows button.row', 'mat-card.player-card', 'p.empty-state', 'p.hint.warn'] }),
  r('turnier', '/profile', 'user', { main: ['app-theme-card .theme-section', 'app-profile-identity-form', 'mat-card.card'] }),   // Darstellung zuoberst (Nutzerwunsch, wie RookHub)
  r('turnier', '/admin', 'user', { main: ['ul.ta-users > li', 'mat-card.ta-card p.muted', 'mat-card.ta-card'] }),
  r('turnier', '/login', 'anon', { main: AUTH_FORM }),
  r('turnier', '/register', 'anon', { main: AUTH_FORM }),
  r('turnier', '/forgot-password', 'anon', { main: AUTH_FORM }),
  r('turnier', '/reset-password?token=ui-sweep', 'anon', { main: AUTH_FORM }),
  r('turnier', '/privacy', 'any', { main: LEGAL }),
  r('turnier', '/impressum', 'any', { main: LEGAL }),
  r('turnier', '/account-deletion', 'any', { main: LEGAL }),

  // ---------------------------------------------------------------- KidHub
  r('kidhub', '/', 'any', { label: 'start', main: ['section.hero a.go', 'section.tiles a.tile', 'kid-error'] }),
  r('kidhub', '/levels', 'any', { main: ['ol.grid .level', 'p.info', 'kid-error'] }),
  r('kidhub', '/levels/1', 'any', { main: KID_PUZZLE }),
  r('kidhub', '/endless', 'any', { main: KID_PUZZLE }),
  r('kidhub', '/stars', 'any', { main: ['ol.grid .stage', 'a.free-btn'] }),
  r('kidhub', '/stars/1', 'any', { main: KID_STARS }),
  r('kidhub', '/stars/free', 'any', { main: KID_STARS }),
  // Freischaltlink aus der Kindergruppe (0.758.0): schaltet Stufe 2 frei und leitet auf /stars/2 weiter.
  r('kidhub', '/stars/2/unlock', 'any', { label: 'stars-unlock', main: KID_STARS }),
  r('kidhub', '/courses', 'any', { main: ['.list a.course', 'section.empty', 'p.info', 'kid-error'] }),
  r('kidhub', '/courses/{kidsBookId}', 'any', { main: KID_PUZZLE }),
  r('kidhub', '/login', 'anon', { main: AUTH_FORM }),
  r('kidhub', '/register', 'anon', { main: AUTH_FORM }),
  r('kidhub', '/forgot-password', 'anon', { main: AUTH_FORM }),
  r('kidhub', '/reset-password?token=ui-sweep', 'anon', { main: AUTH_FORM }),
  r('kidhub', '/privacy', 'any', { main: LEGAL }),
  r('kidhub', '/account-deletion', 'any', { main: LEGAL }),

  // ---------------------------------------------------------------- LeagueHub
  r('leaguehub', '/', 'user', { label: 'start', main: LH_FIXTURE }),
  r('leaguehub', '/verein', 'user', { main: ['table.club-table tr.game', 'section.empty', 'section.gate', 'form.club-search'] }),
  r('leaguehub', '/verein/uebersicht', 'user', { main: ['article.lu-match', 'section.ov-league', 'details.ov-league', 'section.gate', 'p.muted'] }),
  r('leaguehub', '/verein/neu', 'user', { main: LH_UPLOAD }),
  r('leaguehub', '/verein/formular/{scanId}', 'user', { main: LH_SCAN }),
  // 404 auf …/sheet = kein Formular aufbewahrt → Korrektur am Brett (club-scan-page.component.ts, clubSheet-catch).
  r('leaguehub', '/verein/partie/{clubGameId}/korrigieren', 'user', { main: LH_SCAN,
    expectedFailures: [/^GET \/api\/league\/club\/games\/\d+\/sheet(\?club=\d+)? → 404$/] }),
  r('leaguehub', '/konten', 'user', { main: ['article.sugg-group lh-account-suggestions li', 'article.sugg-group', 'section.sugg-page', 'section.gate'] }),
  r('leaguehub', '/uebertragungen', 'user', { main: ['table.bc-list tbody tr', 'form.bc-add', 'section.gate'] }),
  r('leaguehub', '/vereine', 'user', { main: ['table.clubs-tbl tbody tr', 'section.clubs-page', 'section.gate'] }),
  // Unbekannte Adresse (UX-026): Hinweisseite statt Umleitung.
  r('leaguehub', '/diese-seite-gibt-es-nicht', 'any', { label: 'not-found', main: ['section.not-found .nf-actions a', 'section.not-found'] }),
  r('leaguehub', '/s/{leagueShareToken}', 'any', { main: LH_FIXTURE }),
  r('leaguehub', '/s/{leagueShareToken}/hochladen', 'anon', { main: LH_UPLOAD }),
  r('leaguehub', '/s/{leagueShareToken}/formular/{shareScanKey}', 'anon', { main: LH_SCAN }),
  r('leaguehub', '/login', 'anon', { main: AUTH_FORM }),
  r('leaguehub', '/register', 'anon', { main: AUTH_FORM }),
  r('leaguehub', '/forgot-password', 'anon', { main: AUTH_FORM }),
  r('leaguehub', '/reset-password?token=ui-sweep', 'anon', { main: AUTH_FORM }),
  r('leaguehub', '/privacy', 'any', { main: LEGAL }),
  r('leaguehub', '/impressum', 'any', { main: LEGAL }),
  r('leaguehub', '/account-deletion', 'any', { main: LEGAL }),

  // ---------------------------------------------------------------- ClubHub
  r('clubhub', '/', 'user', { label: 'start', main: ['ul.cards > li.kid', '.empty', 'section.gate'] }),
  r('clubhub', '/kind/neu', 'user', { main: ['form.sheet.form', 'section.gate'] }),
  r('clubhub', '/kind/{clubMemberId}', 'user', { main: ['article.sheet', 'form.sheet.form', 'section.gate'] }),
  r('clubhub', '/gruppen', 'user', { main: ['ul.groups > li.group', '.empty', 'form.new-group', 'section.gate'] }),
  r('clubhub', '/gruppen/{clubGroupId}', 'user', { main: ['table.matrix tbody tr', '.empty', 'section.gate'] }),
  r('clubhub', '/gruppen/{clubGroupId}/anwesenheit', 'user', { main: ['ul.roll > li', '.empty', 'section.gate'] }),
  r('clubhub', '/verknuepfen', 'user', { main: ['section.sheet form.form', 'section.sheet section', 'section.sheet'] }),
  r('clubhub', '/login', 'anon', { main: AUTH_FORM }),
  r('clubhub', '/register', 'anon', { main: AUTH_FORM }),
  r('clubhub', '/forgot-password', 'anon', { main: AUTH_FORM }),
  r('clubhub', '/reset-password?token=ui-sweep', 'anon', { main: AUTH_FORM }),
  r('clubhub', '/privacy', 'any', { main: LEGAL }),
  r('clubhub', '/impressum', 'any', { main: LEGAL }),
  r('clubhub', '/account-deletion', 'any', { main: LEGAL }),
];

// Bereiche: `./sweep.sh --area kurse,repertoire` prüft nur diese. Die ERSTE passende Regel gewinnt; eine Route
// ohne Treffer gehört zu „sonstiges". Neue Seite → passt sie nicht in einen Bereich, hier eine Regel ergänzen.
export const AREAS = [
  ['woodpecker', r => r.app === 'rookhub' && /^\/woodpecker/.test(r.url)],
  ['kurse', r => r.app === 'rookhub' && (/^\/(courses|catalog|puzzles\/book|chessable)/.test(r.url) || /slug/.test(r.label ?? ''))],
  ['repertoire', r => r.app === 'rookhub' && /^\/(repertoires|remembered|l\/)/.test(r.url)],
  ['puzzles', r => r.app === 'rookhub' && /^\/(puzzles|favorites|stats|leaderboards)/.test(r.url)],
  ['wochenpost', r => r.app === 'rookhub' && /^\/weekly/.test(r.url)],
  ['partien', r => r.app === 'rookhub' && /^\/(games|g\/|club-games)/.test(r.url)],
  ['analyse', r => r.app === 'rookhub' && /^\/analysis/.test(r.url)],
  ['punktepartie', r => r.app === 'rookhub' && /^\/guess/.test(r.url)],
  ['vorbereitung', r => r.app === 'rookhub' && /^\/prep/.test(r.url)],
  ['aufgabenblaetter', r => r.app === 'rookhub' && /^\/(worksheets|w\/)/.test(r.url)],
  ['ebooks', r => r.app === 'rookhub' && /^\/ebooks/.test(r.url)],
  ['rekonstruktion', r => r.app === 'rookhub' && /^\/(reconstruct|r\/)/.test(r.url)],
  ['anmeldung', r => /^\/(login|register|forgot-password|reset-password)/.test(r.url)],
  ['rechtliches', r => /^\/(privacy|impressum|account-deletion)/.test(r.url)],
  ['konto', r => r.app === 'rookhub' && /^\/(profile|friends|notifications|messages|training-goals|dashboard|admin|help|install)?$|^\/(profile|friends|notifications|messages|training-goals|dashboard|admin|help|install)\b/.test(r.url)],
  ['turnier', r => r.app === 'turnier' || /^\/(tournaments|t\/)/.test(r.url)],
  ['kidhub', r => r.app === 'kidhub'],
  ['leaguehub', r => r.app === 'leaguehub'],
  ['clubhub', r => r.app === 'clubhub'],
];

/** Bereich einer Route (erste passende Regel aus AREAS). */
export function areaOf(route) {
  return AREAS.find(([, test]) => test(route))?.[0] ?? 'sonstiges';
}

/** Stabiler Name einer Route für Dateinamen und Vergleich zwischen Läufen. */
export function routeId(route) {
  if (route.label) return `${route.app}__${route.label}`;
  const slug = route.url.replace(/\?.*$/, '').replace(/[{}]/g, '').replace(/^\/+|\/+$/g, '').replace(/[^a-zA-Z0-9]+/g, '-');
  return `${route.app}__${slug || 'start'}`;
}

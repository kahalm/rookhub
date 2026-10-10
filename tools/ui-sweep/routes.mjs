// Routen-Katalog aller fünf Oberflächen.
//
// Jede Zeile: { app, url, auth, label? }
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

const r = (app, url, auth = 'any', label) => ({ app, url, auth, label });

export const ROUTES = [
  // ---------------------------------------------------------------- RookHub
  r('rookhub', '/', 'any', 'start'),
  r('rookhub', '/login', 'anon'),
  r('rookhub', '/register', 'anon'),
  r('rookhub', '/forgot-password', 'anon'),
  r('rookhub', '/reset-password?token=ui-sweep', 'anon'),
  r('rookhub', '/dashboard', 'user'),
  r('rookhub', '/profile', 'user'),
  r('rookhub', '/friends', 'user'),
  r('rookhub', '/friends/{friendId}/stats', 'user'),
  r('rookhub', '/repertoires', 'user'),
  r('rookhub', '/repertoires/{repertoireId}', 'user'),
  r('rookhub', '/repertoires/{repertoireId}/train', 'user'),
  r('rookhub', '/repertoires/{repertoireId}/flashcards', 'user'),
  r('rookhub', '/puzzles', 'any'),
  r('rookhub', '/puzzles/{puzzleId}', 'any'),
  r('rookhub', '/puzzles/endless', 'any'),
  r('rookhub', '/puzzles/endless/history', 'user'),
  r('rookhub', '/puzzles/book/{bookPuzzleId}', 'any'),
  r('rookhub', '/puzzles/daily/{today}', 'any'),
  r('rookhub', '/favorites', 'user'),
  r('rookhub', '/worksheets', 'user'),
  r('rookhub', '/worksheets/{worksheetId}', 'user'),
  r('rookhub', '/worksheets/{worksheetId}/print', 'user'),
  r('rookhub', '/weekly', 'user'),
  r('rookhub', '/weekly/{weeklyId}', 'user'),
  r('rookhub', '/guess', 'any'),
  r('rookhub', '/guess/{guessId}', 'any'),
  r('rookhub', '/analysis', 'any'),
  r('rookhub', '/analysis/games', 'user'),
  r('rookhub', '/analysis/games/{analysisId}', 'user'),
  r('rookhub', '/analysis/jobs', 'user'),
  r('rookhub', '/analysis/compare/{comparisonId}', 'user'),
  r('rookhub', '/prep', 'user'),
  r('rookhub', '/prep/{prepId}', 'user'),
  r('rookhub', '/games', 'user'),
  r('rookhub', '/games/scoresheet', 'user'),
  r('rookhub', '/games/{gameId}', 'user'),
  r('rookhub', '/games/{gameId}/edit', 'user'),
  r('rookhub', '/games/{analyzedGameId}', 'user', 'games-analyzed'),
  r('rookhub', '/club-games/{clubGameId}', 'user'),
  r('rookhub', '/g/{gameShareToken}', 'any'),
  r('rookhub', '/g/{analyzedShareToken}', 'any', 'g-analyzed'),
  r('rookhub', '/reconstruct', 'user'),
  r('rookhub', '/reconstruct/{reconstructionId}', 'user'),
  r('rookhub', '/r/{reconstructionToken}', 'any'),
  r('rookhub', '/w/{worksheetToken}', 'any'),
  r('rookhub', '/remembered', 'user'),
  r('rookhub', '/stats', 'user'),
  r('rookhub', '/leaderboards', 'user'),
  r('rookhub', '/training-goals', 'user'),
  r('rookhub', '/notifications', 'user'),
  r('rookhub', '/messages', 'user'),
  r('rookhub', '/courses', 'user'),
  r('rookhub', '/catalog', 'user'),
  r('rookhub', '/courses/{bookId}', 'user'),
  r('rookhub', '/courses/{bookId}/browse', 'user'),
  r('rookhub', '/courses/{bookId}/flashcards', 'user'),
  r('rookhub', '/courses/{bookId}/sequential', 'user'),
  r('rookhub', '/courses/{calcBookId}/calc', 'user'),
  r('rookhub', '/chessable', 'user'),
  r('rookhub', '/admin', 'user'),
  r('rookhub', '/help', 'any'),
  r('rookhub', '/install', 'any'),
  r('rookhub', '/privacy', 'any'),
  r('rookhub', '/impressum', 'any'),
  r('rookhub', '/account-deletion', 'any'),
  r('rookhub', '/tournaments', 'any', 'tournaments-moved'),
  r('rookhub', '/diese-seite-gibt-es-nicht/xyz', 'any', 'not-found'),

  // ---------------------------------------------------------------- Turnierseite
  r('turnier', '/tournaments/calendar', 'any'),
  r('turnier', '/tournaments/calendar/{directoryId}', 'any'),
  r('turnier', '/tournaments/{tournamentId}', 'any'),
  r('turnier', '/t/{tournamentId}', 'any'),
  r('turnier', '/tournaments', 'user'),
  r('turnier', '/tournaments/history', 'user'),
  r('turnier', '/profile', 'user'),
  r('turnier', '/admin', 'user'),
  r('turnier', '/login', 'anon'),
  r('turnier', '/register', 'anon'),
  r('turnier', '/privacy', 'any'),
  r('turnier', '/impressum', 'any'),

  // ---------------------------------------------------------------- KidHub
  r('kidhub', '/', 'any', 'start'),
  r('kidhub', '/levels', 'any'),
  r('kidhub', '/levels/1', 'any'),
  r('kidhub', '/endless', 'any'),
  r('kidhub', '/stars', 'any'),
  r('kidhub', '/stars/1', 'any'),
  r('kidhub', '/stars/free', 'any'),
  r('kidhub', '/courses', 'any'),
  r('kidhub', '/courses/{kidsBookId}', 'any'),
  r('kidhub', '/login', 'anon'),
  r('kidhub', '/privacy', 'any'),

  // ---------------------------------------------------------------- LeagueHub
  r('leaguehub', '/', 'user', 'start'),
  r('leaguehub', '/verein', 'user'),
  r('leaguehub', '/verein/neu', 'user'),
  r('leaguehub', '/konten', 'user'),
  r('leaguehub', '/uebertragungen', 'user'),
  r('leaguehub', '/vereine', 'user'),
  r('leaguehub', '/s/{leagueShareToken}', 'any'),
  r('leaguehub', '/s/{leagueShareToken}/hochladen', 'anon'),
  r('leaguehub', '/login', 'anon'),
  r('leaguehub', '/privacy', 'any'),
  r('leaguehub', '/impressum', 'any'),

  // ---------------------------------------------------------------- ClubHub
  r('clubhub', '/', 'user', 'start'),
  r('clubhub', '/kind/neu', 'user'),
  r('clubhub', '/kind/{clubMemberId}', 'user'),
  r('clubhub', '/gruppen', 'user'),
  r('clubhub', '/gruppen/{clubGroupId}', 'user'),
  r('clubhub', '/gruppen/{clubGroupId}/anwesenheit', 'user'),
  r('clubhub', '/verknuepfen', 'user'),
  r('clubhub', '/login', 'anon'),
  r('clubhub', '/privacy', 'any'),
];

// Bereiche: `./sweep.sh --area kurse,repertoire` prüft nur diese. Die ERSTE passende Regel gewinnt; eine Route
// ohne Treffer gehört zu „sonstiges". Neue Seite → passt sie nicht in einen Bereich, hier eine Regel ergänzen.
export const AREAS = [
  ['kurse', r => r.app === 'rookhub' && /^\/(courses|catalog|puzzles\/book|chessable)/.test(r.url)],
  ['repertoire', r => r.app === 'rookhub' && /^\/(repertoires|remembered)/.test(r.url)],
  ['puzzles', r => r.app === 'rookhub' && /^\/(puzzles|favorites|stats|leaderboards)/.test(r.url)],
  ['wochenpost', r => r.app === 'rookhub' && /^\/weekly/.test(r.url)],
  ['partien', r => r.app === 'rookhub' && /^\/(games|g\/|club-games)/.test(r.url)],
  ['analyse', r => r.app === 'rookhub' && /^\/analysis/.test(r.url)],
  ['punktepartie', r => r.app === 'rookhub' && /^\/guess/.test(r.url)],
  ['vorbereitung', r => r.app === 'rookhub' && /^\/prep/.test(r.url)],
  ['aufgabenblaetter', r => r.app === 'rookhub' && /^\/(worksheets|w\/)/.test(r.url)],
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

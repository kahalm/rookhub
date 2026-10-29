// Zustände NACH einer Bedienung: Menüs, Dialoge, Reiter, Formularfehler, Brett nach einem Zug …
//
// Der Routen-Katalog (routes.mjs) fotografiert jede Seite so, wie sie nach dem Laden aussieht. Ein Zustand hier hängt an
// einer Katalog-Route (`of` = deren Id, siehe `node sweep.mjs --list`), lädt dieselbe Adresse, führt `steps` aus und
// fotografiert dann — als eigene Aufnahme mit der Id `<Route>--<name>` (Bericht, Vergleich, --route-Filter wie gewohnt).
//
//   of     — Id der Katalog-Route
//   name   — kurzer Name des Zustands (Teil der Bild-Datei)
//   auth   — nur diese Rollen (Vorgabe: die der Route). Zustände, die etwas SCHREIBEN würden, gibt es hier nicht —
//            Dialoge werden geöffnet, nie bestätigt; Formulare werden nie abgeschickt, außer sie scheitern absichtlich
//            (falsches Kennwort).
//            Sicherheitsnetz: solange ein Zustand läuft, beantwortet das Werkzeug JEDE schreibende API-Anfrage
//            (POST/PUT/PATCH/DELETE außer /api/auth/*) selbst mit 200 „{}" — sie erreicht den Server nicht. „Aufgeben",
//            ein Zug angemeldet auf dem Analysebrett oder der Endlos-Start verändern so keine Testdaten.
//   steps  — der Reihe nach:
//              { click: sel }           erstes passendes Element anklicken
//              { clickIf: sel }         dito, aber nur wenn sichtbar (z. B. Spielweise-Dialog, der nur beim ersten Mal kommt)
//              { select: sel, value }   Auswahlliste (natives <select>)
//              { fill: sel, text }      Feld füllen
//              { press: key }           Taste (z. B. 'Escape', 'Enter'); mit sel: auf diesem Element
//              { hover: sel }
//              { blur: sel }            Fokus weg (Pflichtfeld-Fehler erscheinen erst danach)
//              { waitFor: sel }         warten, bis sichtbar (höchstens 8 s)
//              { wait: ms }
//              { move: 'e2e4', board?: sel }  Zug auf dem Brett (chessground: Klick Feld → Feld, Ausrichtung beachtet)
//            Selektoren sind Playwright-Selektoren (CSS, `:has-text("…")`, `>> nth=1` …).
//   expect — Selektor, der danach sichtbar sein muss; fehlt er, steht „Zustand nicht erreicht" im Bericht (die Aufnahme
//            gibt es trotzdem — sie zeigt, was stattdessen da ist).
//   fresh  — ohne vorbelegte Spielweise (sweep.mjs legt sonst „Einfachmodus" in den Speicher, damit der Dialog nicht
//            jede Puzzle-/Kursaufnahme verdeckt) — nur für den Zustand, der genau diesen Dialog zeigt.
//   once   — schreibende Adressen (RegExp auf den Pfad), die EINMAL je Rolle/Sprache wirklich zum Server gehen; die
//            Antwort (auch ein Fehler) wird für alle übrigen Varianten wiedergegeben. Für die gewollt scheiternde
//            Anmeldung: 20 echte Fehlversuche liefen sonst in die Anmelde-Drossel (429) statt in „Kennwort falsch".
//   main   — Hauptsache für die Falz-Prüfung (Vorgabe: keine; Zustände sind von der Falz-Prüfung ausgenommen).
//
// Ein Schritt, der scheitert (Selektor fehlt), bricht die Schritte ab; die Aufnahme zeigt den Stand bis dahin.

const MENU = '.cdk-overlay-pane .mat-mdc-menu-panel';
const DIALOG = 'mat-dialog-container';
/** Inhalt IM Dialog: der Titel der Dialog-Komponente (`comp` = deren Selektor) — nicht nur der leere Rahmen. */
const dlg = (comp, inner = ':is([mat-dialog-title], .dialog-title)') => `${DIALOG} ${comp} ${inner}`;
/** Menüpunkt eines offenen Menüs über sein Symbol (sprachunabhängig). */
const menuItem = icon => `.mat-mdc-menu-panel button.mat-mdc-menu-item:has(mat-icon:text-is("${icon}"))`;

// Spielweise-Dialog (Trainings-/Einfachmodus): sweep.mjs belegt die Wahl vor; falls ein Bereich trotzdem fragt
// (eigene Wahl je Kurs/Wochenpost), vor den eigentlichen Schritten wegklicken (Einfachmodus: schreibt nur localStorage).
const EASY = { clickIf: '.solve-mode-panel .sm-choices > .sm-choice:nth-child(2)' };
const SOLVE_MODE = '.cdk-overlay-pane.solve-mode-panel mat-dialog-container .sm-choices';
const LOGGED_IN = ['user', 'admin'];
// LeagueHub: nur Konten mit Vereinszugang (die anderen sehen die Sperrseite); ClubHub: Admin + Trainer der Gruppe.
const LEAGUE = ['user', 'admin'];
const CLUB = ['admin', 'trainer'];

export const STATES = [
  // ---------------------------------------------------------------- RookHub: Kopfleiste
  { of: 'rookhub__dashboard', name: 'menue', steps: [{ click: 'mat-toolbar button.msg-mail' }], expect: MENU },
  { of: 'rookhub__dashboard', name: 'menue-konto', auth: LOGGED_IN,
    steps: [{ click: 'mat-toolbar button.msg-mail' }, { click: '.mat-mdc-menu-panel button:has(mat-icon:text-is("account_circle"))' }],
    // über die Symbole statt der Beschriftung — sprachunabhängig (im --lang-en-Lauf scheiterte „Konto“, 17.10.2026)
    expect: '.mat-mdc-menu-panel button.changelog-item' },
  { of: 'rookhub__dashboard', name: 'glocke', steps: [{ click: 'mat-toolbar button.notif-bell' }], expect: '.mat-mdc-menu-panel.notif-menu' },
  { of: 'rookhub__puzzles', name: 'menue-gast', auth: ['anon'], steps: [EASY, { click: 'mat-toolbar button.anon-menu' }], expect: MENU },

  // ---------------------------------------------------------------- RookHub: Partien
  { of: 'rookhub__games-gameId', name: 'menue', steps: [{ click: '.shared-page .header button.game-menu' }], expect: MENU },
  { of: 'rookhub__games', name: 'zeilenmenue', auth: LOGGED_IN,
    steps: [{ click: '.games-page .row.game .actions button.mat-mdc-menu-trigger' }], expect: MENU },
  { of: 'rookhub__games', name: 'loeschen-dialog', auth: LOGGED_IN,
    steps: [{ click: '.games-page .row.game .actions button.mat-mdc-menu-trigger' },
      { click: '.mat-mdc-menu-panel button.mat-mdc-menu-item:has(mat-icon:text-is("delete"))' }],
    expect: 'mat-dialog-container .confirm-text' },

  // ---------------------------------------------------------------- RookHub: Puzzles + Kurse
  { of: 'rookhub__puzzles-puzzleId', name: 'spielweise', fresh: true, steps: [{ waitFor: SOLVE_MODE }], expect: SOLVE_MODE },
  { of: 'rookhub__puzzles-puzzleId', name: 'tipp',
    // Der Tipp-Text steht seit b1 NEBEN dem Slot: unter dem Brett (.hint-list--board) bzw. ab 900 px in der Seitenspalte
    // (.hint-list--side) — je Breite ist genau eine Liste sichtbar.
    steps: [EASY, { click: '.board-hint-slot--board button.board-hint-tip' }], expect: 'ol.hint-list li:visible' },
  { of: 'rookhub__puzzles-puzzleId', name: 'aufgegeben',
    steps: [EASY, { click: 'app-puzzle-status-card .ytp-actions button[color="warn"]' }], expect: 'app-puzzle-status-card .psc-failed' },
  { of: 'rookhub__courses-bookId-sequential', name: 'aufgegeben',
    steps: [EASY, { click: 'app-puzzle-status-card .ytp-actions button[color="warn"]' }], expect: 'app-puzzle-status-card .psc-failed' },
  { of: 'rookhub__courses-bookId-sequential', name: 'uebersprungen',
    steps: [EASY, { click: 'app-puzzle-status-card button.ytp-skip' }], expect: 'app-puzzle-status-card .psc-card' },
  // Konten mit offenem Lauf (user aus seed-user.mjs) sehen statt „Starten" den Fortsetzen-Kasten (b1-rk-endless-fortsetzen-kasten).
  { of: 'rookhub__puzzles-endless', name: 'gestartet',
    steps: [{ click: '.config-screen button.start-btn-top, .config-screen .resume-banner button.start-btn' }, EASY],
    expect: '.play-screen app-puzzle-board' },
  { of: 'rookhub__stats', name: 'kurse', auth: [...LOGGED_IN, 'empty'],
    steps: [{ click: '.stats-container .mode-toggle mat-button-toggle[value="course"] button' }],
    expect: '.mode-toggle mat-button-toggle[value="course"].mat-button-toggle-checked' },

  // ---------------------------------------------------------------- RookHub: Repertoire
  { of: 'rookhub__repertoires-repertoireId', name: 'linienmenue', auth: LOGGED_IN,
    steps: [{ click: 'app-repertoire-lines .line-item button.line-menu-btn' }], expect: MENU },
  { of: 'rookhub__repertoires-train-learn', name: 'weiter', auth: LOGGED_IN,
    // „Diesen Zug einprägen“ steht beim Laden schon da; ohne Kommentar zum Zug geht es nach 1 s von selbst weiter.
    steps: [{ press: 'Space' }, { waitFor: 'p.hint' }], expect: 'p.hint' },

  // ---------------------------------------------------------------- RookHub: Analyse
  { of: 'rookhub__analysis', name: 'zug-e4', steps: [{ move: 'e2e4', board: 'app-analysis-board cg-board' }],
    expect: 'app-analysis-board cg-board square.last-move' },
  { of: 'rookhub__analysis', name: 'engine-aus',
    steps: [{ click: '.engine-card .engine-head mat-slide-toggle button[role="switch"]' }],
    expect: '.engine-card mat-slide-toggle button[aria-checked="false"]' },
  { of: 'rookhub__analysis', name: 'hilfe', steps: [{ click: '.engine-card app-help-hint button.hh-btn' }], expect: '.mat-mdc-tooltip.hh-tooltip' },
  { of: 'rookhub__analysis-games', name: 'hilfe', auth: LOGGED_IN,
    steps: [{ click: '.ga-container .header h1 app-help-hint button.hh-btn' }], expect: '.mat-mdc-tooltip.hh-tooltip' },

  // ---------------------------------------------------------------- RookHub: Konto, Freunde, Aufgabenblätter
  { of: 'rookhub__friends', name: 'anfragen', auth: [...LOGGED_IN, 'empty'],
    steps: [{ click: '.friends-tabs .mat-mdc-tab-labels > [role="tab"]:nth-child(2)' }],
    expect: '.friends-tabs [role="tab"]:nth-child(2)[aria-selected="true"]' },
  { of: 'rookhub__profile', name: 'pflichtfeld', auth: LOGGED_IN,
    steps: [{ fill: '.profile-form input[name="email"]', text: 'ui-sweep-neu@example.org' },
      { waitFor: '.profile-form input[name="currentPassword"]' }, { click: '.profile-form button[type="submit"]' }],
    expect: '.profile-form mat-form-field.mat-form-field-invalid' },
  { of: 'rookhub__worksheets-worksheetId', name: 'qr', auth: LOGGED_IN,
    steps: [{ click: '.wd-meta button.wd-qr-toggle' }], expect: 'mat-card.wd-share app-qr-code' },
  { of: 'rookhub__worksheets-worksheetId', name: 'menue', auth: LOGGED_IN,
    steps: [{ click: '.wd-toolbar button.mat-mdc-menu-trigger' }], expect: MENU },

  // ---------------------------------------------------------------- E-Books + Woodpecker
  { of: 'rookhub__ebooks', name: 'hochladen', steps: [{ click: '.eb-page .header-actions button.eb-upload' }], expect: DIALOG },
  { of: 'rookhub__ebooks', name: 'admin-menue', auth: ['admin'],
    steps: [{ click: '.eb-page .header-actions button[mat-icon-button]' }], expect: MENU },
  // Seit c1 steht der Schalter in .eb-filter-more (am Handy hinter „Filter ▾“, ab 600 px offen in der Zeile). Die
  // Filterzeile gibt es nur, wenn die Rolle mindestens ein Buch sieht — seed.mjs gibt zwei für „Everyone" frei.
  // 0.822.0: Elo-Spanne statt „Mit Diagrammen“ — ab 1800 filtern. Seit 0.877.0 steht sie auf jeder Breite in der
  // Filter-Komponente hinter „Filter ▾“ (erst aufklappen). Geprüft wird das ausgefüllte Feld.
  { of: 'rookhub__ebooks', name: 'elo-filter',
    steps: [{ clickIf: '.eb-filters button.eb-filter-toggle' }, { fill: '.eb-elo input >> nth=0', text: '1800' }],
    expect: '.eb-elo input.ng-dirty' },
  { of: 'rookhub__ebooks-ebookId', name: 'menue', auth: ['admin'], steps: [{ click: '.rd-toolbar > button[mat-icon-button]' }], expect: MENU },
  // Brett im Leser (0.777.0 ff.): erstes Diagramm aufs Brett (am Handy erst den Brett-Knopf), dann die Modi.
  ...[['brett', null], ['frei', 1], ['kalkulieren', 2], ['loesen', 3], ['maia', 4]].map(([name, nth]) => ({
    of: 'rookhub__ebooks-diagramme', name,
    steps: [{ clickIf: '.rd-toolbar .rd-diag-btn' }, { click: '.rd-panel-head button.rd-jump >> nth=0' },
      { waitFor: '.rd-panel app-ebook-board' }, ...(nth == null ? [] : [{ click: `.rd-panel .eb-modes .eb-mode >> nth=${nth}` }])],
    expect: nth == null ? '.rd-panel app-ebook-board' : `.rd-panel .eb-modes .eb-mode:nth-child(${nth + 1}).eb-mode--on`,
  })),
  // Nur Konten mit lösbaren Kursen: ohne Kurs führt die leere Übersicht seit 0.807.0 direkt zu den Kursen (kein Dialog).
  { of: 'rookhub__woodpecker', name: 'anlegen', auth: ['user', 'admin'],
    steps: [{ click: '.page .header-actions button, .page .empty-state button' }], expect: DIALOG },
  { of: 'rookhub__woodpecker', name: 'plan-menue', auth: ['admin'], steps: [{ click: '.plans .plan .plan-head button[mat-icon-button]' }], expect: MENU },

  // ---------------------------------------------------------------- Dialoge (Runde 28.10.2026)
  // Je Dialog EIN Zustand: öffnen, nie bestätigen. `expect` prüft einen Inhalt IM Dialog (Titel der Komponente), nicht
  // nur den leeren Rahmen. Speichert ein Dialog beim Öffnen etwas (Markieren vor „Notizen“), stellt das Sicherheitsnetz
  // die Antwort (writeDecision) — der Server sieht nichts.
  // Partien
  { of: 'rookhub__games', name: 'pgn-import', auth: LOGGED_IN,
    steps: [{ click: '.games-page button.pgn-upload, .games-page button.empty-upload' }], expect: dlg('app-pgn-import-dialog') },
  { of: 'rookhub__games', name: 'roast', auth: LOGGED_IN,
    steps: [{ click: '.games-page .row.game .actions button.mat-mdc-menu-trigger' }, { click: menuItem('local_fire_department') }],
    expect: dlg('app-game-roast-dialog') },
  // Analyse
  { of: 'rookhub__analysis', name: 'verlauf', auth: LOGGED_IN,
    steps: [{ click: '.header button.history-btn' }], expect: dlg('app-analysis-history-dialog') },
  { of: 'rookhub__analysis', name: 'zuege-vergleichen', auth: LOGGED_IN,
    steps: [{ click: 'app-position-menu button.position-menu-btn' }, { click: '.mat-mdc-menu-panel button.pm-compare' }],
    expect: dlg('app-move-compare-dialog') },
  { of: 'rookhub__analysis', name: 'markieren-notizen', auth: LOGGED_IN,
    steps: [{ click: 'app-mark-position-button button.mark' }], expect: dlg('app-mark-notes-dialog') },
  { of: 'rookhub__games-analyzed', name: 'tiefe-analyse', auth: LOGGED_IN,
    steps: [{ click: 'app-position-menu.nav-menu button.position-menu-btn' }, { click: '.mat-mdc-menu-panel button.pm-deep' }],
    expect: dlg('app-deep-analysis-dialog') },
  { of: 'rookhub__analysis-games', name: 'knoten', auth: ['admin'],
    steps: [{ clickIf: '.ga-container button.add-btn' }, { fill: '.new-card textarea', text: '1. e4 e5 2. Nf3 Nc6 *' },
      { click: '.new-card button.nodes-btn' }], expect: dlg('app-game-analysis-nodes-dialog') },
  // Repertoire
  { of: 'rookhub__repertoires', name: 'anlegen', auth: LOGGED_IN,
    steps: [{ click: '.repertoire-container .header button[color="primary"]' }], expect: dlg('app-create-repertoire-dialog') },
  { of: 'rookhub__repertoires', name: 'bearbeiten', auth: LOGGED_IN,
    steps: [{ click: '.rep-card .rep-actions button.mat-mdc-menu-trigger' }, { click: menuItem('edit') }],
    expect: dlg('app-create-repertoire-dialog') },
  { of: 'rookhub__repertoires', name: 'teilen', auth: LOGGED_IN,
    steps: [{ click: '.rep-card .rep-actions button.mat-mdc-menu-trigger' }, { click: menuItem('group_add') }],
    expect: dlg('app-share-repertoire-dialog') },
  { of: 'rookhub__repertoires-repertoireId', name: 'teilauswahl', auth: LOGGED_IN,
    steps: [{ click: 'app-repertoire-lines .course-bar button.mat-mdc-menu-trigger' }, { click: menuItem('library_add') }],
    expect: dlg('app-create-subset-dialog') },
  { of: 'rookhub__repertoires-repertoireId-train', name: 'intervalle', auth: LOGGED_IN,
    steps: [{ click: 'button.more.mat-mdc-menu-trigger' }, { click: menuItem('tune') }], expect: dlg('app-sr-config-dialog') },
  // Kurse
  { of: 'rookhub__courses', name: 'anlegen', auth: LOGGED_IN,
    steps: [{ click: '.header-actions button[color="primary"]' }], expect: dlg('app-create-course-dialog') },
  { of: 'rookhub__courses', name: 'partien-importieren', auth: LOGGED_IN,
    steps: [{ click: '.header-actions button[mat-stroked-button]' }], expect: dlg('app-create-course-dialog') },
  { of: 'rookhub__courses', name: 'verknuepfen', auth: LOGGED_IN,
    steps: [{ click: 'app-course-card .util-actions button.mat-mdc-menu-trigger' },
      { click: '.mat-mdc-menu-panel button.mat-mdc-menu-item:has(mat-icon:text-matches("^(add_)?link$"))' }],
    expect: dlg('app-link-course-dialog') },
  { of: 'rookhub__courses', name: 'themen', auth: ['admin'],
    steps: [{ click: 'app-course-card .util-actions button.mat-mdc-menu-trigger' }, { click: menuItem('sell') }],
    expect: dlg('app-course-themes-dialog') },
  { of: 'rookhub__courses-bookId', name: 'kapitel-anlegen', auth: ['admin'],
    steps: [{ click: '.chapters-card .section-head button' }], expect: dlg('app-add-lines-dialog') },
  { of: 'rookhub__courses-bookId', name: 'wochenpost', auth: ['admin'],
    steps: [{ click: 'app-chapter-row .chapter-actions button.mat-mdc-menu-trigger' }, { click: '.mat-mdc-menu-panel button.weekly-from-chapter' }],
    expect: dlg('app-weekly-from-chapter-dialog') },
  { of: 'rookhub__courses-calc-detail', name: 'ausgabe', auth: ['admin'],
    steps: [{ click: 'app-chapter-row .chapter-actions button.mat-mdc-menu-trigger' }, { click: menuItem('event') }],
    expect: dlg('app-calc-edition-dialog') },
  { of: 'rookhub__courses-calc-detail', name: 'verteiler', auth: ['admin'],
    steps: [{ click: '.head-card button.mat-mdc-menu-trigger' }, { click: menuItem('group') }], expect: dlg('app-calc-members-dialog') },
  { of: 'rookhub__courses-calcBookId-calc', name: 'einstellungen',
    steps: [{ click: 'button.calc-settings-bar:visible, button.calc-rail-btn:visible' }], expect: dlg('app-calc-settings-dialog') },
  { of: 'rookhub__courses-calcBookId-calc', name: 'zeit', steps: [{ click: 'button.calc-timer-btn' }], expect: dlg('app-calc-timer-dialog') },
  { of: 'rookhub__courses-calcBookId-calc', name: 'ergebnis', steps: [{ click: 'button.calc-result-btn' }], expect: dlg('app-calc-grade-dialog') },
  // Puzzles
  { of: 'rookhub__puzzles-puzzleId', name: 'teilen', steps: [EASY, { click: 'app-puzzle-action-bar button.pab-share' }],
    expect: dlg('app-share-puzzle-dialog') },
  { of: 'rookhub__puzzles-puzzleId', name: 'einstellungen',
    steps: [EASY, { click: 'app-puzzle-action-bar button.pab-more' }, { click: menuItem('settings') }], expect: dlg('app-puzzle-settings-dialog') },
  { of: 'rookhub__puzzles-endless', name: 'hilfe', steps: [{ click: '.config-screen button.help-btn' }], expect: dlg('app-endless-help-dialog') },
  // Wochenpost, Profil, Admin
  { of: 'rookhub__weekly', name: 'aus-kapitel', auth: ['admin'],
    steps: [{ click: '.header-actions button.upload-toggle' }, { click: '.upload-card .or-chapter button' }],
    expect: dlg('app-weekly-from-chapter-dialog') },
  { of: 'rookhub__profile', name: 'token-anlegen', auth: LOGGED_IN,
    steps: [{ click: 'app-api-tokens button.tokens-create' }], expect: dlg('app-create-token-dialog') },
  // Konto „claude-dev-user“: gibt es immer, ist nie das eigene (sich selbst sperren geht nicht). Breit: Knopf in der
  // Tabelle; am Handy: Karte → ⋮ → „Sperren …“ (Symbol „block“).
  { of: 'rookhub__admin', name: 'sperren', auth: ['admin'],
    steps: [{ clickIf: 'app-admin-users-tab tr:has-text("claude-dev-user") button.lock-btn:visible' },
      { clickIf: 'app-admin-users-tab .user-card:has(.uc-name:text-is("claude-dev-user")) .uc-actions button.mat-mdc-menu-trigger:visible' },
      { clickIf: menuItem('block') }],
    expect: dlg('app-lock-user-dialog') },
  // E-Books
  { of: 'rookhub__ebooks', name: 'bearbeiten', auth: ['admin'],
    steps: [{ click: 'mat-card.eb-card:not(.eb-card--folder) button.mat-mdc-menu-trigger' }, { click: menuItem('edit') }],
    expect: dlg('app-ebook-edit-dialog') },
  { of: 'rookhub__ebooks', name: 'sichtbarkeit', auth: ['admin'],
    steps: [{ click: 'mat-card.eb-card:not(.eb-card--folder) button.mat-mdc-menu-trigger' }, { click: menuItem('visibility') }],
    expect: dlg('app-ebook-visibility-dialog') },
  { of: 'rookhub__ebooks', name: 'sammlung', auth: ['admin'],
    steps: [{ click: '.eb-page .header-actions button[aria-pressed]' }, { click: 'mat-checkbox.eb-select input' },
      { click: '.eb-bulk button:has(mat-icon:text-is("create_new_folder"))' }],
    expect: dlg('app-ebook-collection-dialog') },
  { of: 'rookhub__ebooks-diagramme', name: 'stellung-korrigieren', auth: ['admin'],
    steps: [{ clickIf: '.rd-toolbar .rd-diag-btn' }, { click: '.rd-panel-head button.rd-jump >> nth=0' },
      { waitFor: '.rd-panel app-ebook-board' }, { click: '.rd-panel app-ebook-board button.eb-more' }, { click: '.mat-mdc-menu-panel button.eb-correct-item' }],
    expect: dlg('app-ebook-position-dialog') },
  // Turnierseite
  { of: 'turnier__tournaments-tournamentId', name: 'teilen',
    steps: [{ clickIf: 'button.wide-only:has(mat-icon:text-is("share")):visible' }, { clickIf: 'button.more-actions:visible' },
      { clickIf: menuItem('share') }],
    expect: dlg('app-share-tournament-dialog') },
  { of: 'turnier__tournaments-tournamentId', name: 'mannschaft',
    steps: [{ click: 'app-tournament-tables .mat-mdc-tab-labels > [role="tab"]:nth-child(2)' }, { click: 'app-tournament-tables .team-link:visible' }],
    expect: dlg('app-team-players-dialog') },
  { of: 'turnier__tournaments-calendar', name: 'kalender-karte',
    steps: [{ click: '.mat-mdc-tab-labels > [role="tab"]:last-child' }, { click: 'app-tournament-calendar :is(.cal-entry, .agenda-entry):visible' }],
    expect: `${DIALOG} app-tournament-card-dialog app-tournament-card` },
  { of: 'turnier__tournaments-calendar', name: 'fehlt-melden', auth: LOGGED_IN,
    steps: [{ click: '.missing-row button' }], expect: dlg('app-missing-tournament-dialog') },
  { of: 'turnier__tournaments-calendar', name: 'suchprofil', auth: LOGGED_IN,
    steps: [{ click: 'app-search-profile-menu button.mat-mdc-menu-trigger' }, { click: menuItem('add_location_alt') }],
    expect: dlg('app-search-profile-dialog') },
  { of: 'turnier__tournaments-calendar-directoryId', name: 'melden', auth: LOGGED_IN,
    steps: [{ click: 'button.report-btn' }], expect: dlg('app-report-entry-dialog') },
  { of: 'turnier__tournaments-history', name: 'spieler-verfolgen', auth: LOGGED_IN,
    steps: [{ click: 'button.track-add' }], expect: dlg('app-track-player-dialog') },

  // ---------------------------------------------------------------- Anmeldung (dieselbe Komponente in allen Oberflächen)
  // Falsches Kennwort mit einem Namen, den es nicht gibt — kein echtes Konto wird gedrosselt.
  { of: 'rookhub__login', name: 'falsches-kennwort',
    steps: [{ fill: 'form.auth-form input[name="username"]', text: 'ui-sweep-gibt-es-nicht' },
      { fill: 'form.auth-form input[name="password"]', text: 'falsch123' }, { click: 'form.auth-form button[type="submit"]' }],
    once: [/^\/api\/auth\/login$/], expect: '.form-error[role="alert"]' },
  { of: 'rookhub__login', name: 'leer', steps: [{ click: 'form.auth-form button[type="submit"]' }], expect: 'form.auth-form mat-error' },
  { of: 'rookhub__register', name: 'leer', steps: [{ click: 'form.auth-form button[type="submit"]' }], expect: 'form.auth-form mat-error' },

  // ---------------------------------------------------------------- Turnierseite
  { of: 'turnier__tournaments-calendar', name: 'filter', steps: [{ click: '.filter-bar button.filter-toggle' }], expect: '.filter-extra' },
  { of: 'turnier__tournaments-tournamentId', name: 'teams',
    steps: [{ click: 'app-tournament-tables .mat-mdc-tab-labels > [role="tab"]:nth-child(2)' }],
    expect: 'app-tournament-tables [role="tab"]:nth-child(2)[aria-selected="true"]' },
  { of: 'turnier__tournaments-tournamentId', name: 'paarungen',
    steps: [{ click: 'app-tournament-tables .mat-mdc-tab-labels > [role="tab"]:nth-child(3)' }],
    expect: 'app-tournament-tables [role="tab"]:nth-child(3)[aria-selected="true"]' },

  // ---------------------------------------------------------------- KidHub
  { of: 'kidhub__start', name: 'englisch', steps: [{ select: 'footer.foot label.lang select', value: 'en' }], expect: 'html[lang="en"]' },
  { of: 'kidhub__levels-1', name: 'tipp',
    steps: [{ click: 'kid-puzzle .actions button.big.hint:not([disabled])' }], expect: 'kid-puzzle cg-container svg.cg-shapes g' },

  // ---------------------------------------------------------------- LeagueHub
  { of: 'leaguehub__verein', name: 'zeilenmenue', auth: LEAGUE,
    steps: [{ click: 'table.club-table tr.game td.acts button.more-btn:visible, .gc-acts button.more-btn:visible' }], expect: MENU },
  { of: 'leaguehub__verein', name: 'info', auth: LEAGUE, steps: [{ click: 'p.info-line button.chk-btn' }], expect: '.info-panel .note' },
  // Vereinsübersicht (28.10.2026): Nachspielen einer Partie, das „+n“-Stellungsfenster einer Runde und „In der Partie zeigen“.
  // Die „+“-Stellung legt seed.mjs an (overviewMarkGameId).
  { of: 'leaguehub__verein-uebersicht', name: 'nachspielen', auth: LEAGUE,
    steps: [{ click: '.lu-board .bg-replay >> nth=0' }], expect: '.bg-replay-box lh-game-replay' },
  { of: 'leaguehub__verein-uebersicht', name: 'plus-fenster', auth: LEAGUE,
    steps: [{ click: '.ov-round .gb-mark >> nth=0' }, { waitFor: 'lh-marked-positions-dialog .mp-board' }],
    expect: dlg('lh-marked-positions-dialog', '.mp-board') },
  // Seit 0.865.0 öffnet das Fenster zum Lesen (Schlagworte + Bemerkung); Bearbeiten erst über den Bleistift.
  { of: 'leaguehub__verein-uebersicht', name: 'plus-fenster-bearbeiten', auth: LEAGUE,
    steps: [{ click: '.ov-round .gb-mark >> nth=0' }, { waitFor: 'lh-marked-positions-dialog .mp-board' },
      { click: 'lh-marked-positions-dialog button.mn-pencil' }],
    expect: 'lh-marked-positions-dialog textarea.mn-text' },
  { of: 'leaguehub__verein-uebersicht', name: 'in-partie-zeigen', auth: LEAGUE,
    steps: [{ click: '.ov-round .gb-mark >> nth=0' }, { waitFor: 'lh-marked-positions-dialog .mp-board' },
      { click: 'lh-marked-positions-dialog .mat-mdc-dialog-actions button.btn-link >> nth=0' }],
    expect: '.bg-replay-box lh-game-replay' },
  { of: 'leaguehub__konten', name: 'info', auth: ['admin'], steps: [{ click: 'button.chk-btn[aria-label="Wie gesucht wird"]' }], expect: '.info-panel.sugg-info' },
  { of: 'leaguehub__start', name: 'prognose-info', auth: LEAGUE,
    steps: [{ click: 'lh-fixture button.chk-btn[aria-label="Wie die Prognose zustande kommt"]' }], expect: 'lh-fixture .info-panel' },
  { of: 'leaguehub__start-gespielt', name: 'brettmenue', auth: LEAGUE,
    steps: [{ click: 'lh-fixture td.pg button.more-btn >> nth=0' }], expect: MENU },
  { of: 'leaguehub__start', name: 'kader', auth: LEAGUE,
    steps: [{ click: 'lh-fixture details.roster > summary' }], expect: 'lh-fixture details.roster[open] .rtable' },

  // ---------------------------------------------------------------- ClubHub
  { of: 'clubhub__gruppen-clubGroupId', name: 'kinder-verwalten', auth: CLUB,
    steps: [{ click: 'details.manage-kids > summary' }], expect: 'details.manage-kids[open] section' },
  { of: 'clubhub__gruppen-clubGroupId', name: 'gruppe-aendern', auth: ['admin'],
    steps: [{ click: 'details.manage-group > summary' }], expect: 'details.manage-group[open] section.form' },
  { of: 'clubhub__kind-neu', name: 'leer', auth: CLUB,
    // Leerer Vorname: Fehler direkt unter dem Feld (p.err.need), die Statuszeile bleibt für Serverfehler.
    steps: [{ click: 'form.sheet.form .form-save button[type="submit"]' }], expect: 'form.sheet.form p.err.need' },
];

/**
 * POSTs, die nur LESEN (Puzzle-Vorrat für Endlos, KidHub-Endlos): sie gehen auch in Zuständen zum Server. Mit dem
 * gestellten „{}" wartete der Endlos-Start ewig auf Puzzles (Spinner im Bild).
 */
// Lese-POSTs (Körper = Abfrage, nichts wird geschrieben) gehen zum Server — ein Echo des Körpers bräche die App:
// same-game lieferte so ein Objekt statt einer Liste („a.filter is not a function“ im ⋮ der Partie, Volllauf 17.10.2026).
export const READ_POSTS = [/^\/api\/puzzles\/random-batch$/, /^\/api\/kids\/endless\/batch$/, /^\/api\/game-analyses\/same-game$/];

/**
 * Was das Werkzeug in einem Zustand mit einer Anfrage tut: 'pass' (zum Server), 'once' (einmal zum Server, dann aus dem
 * Zwischenspeicher) oder 'stub' (selbst beantworten, Körper aus `stubBody`).
 */
export function writeDecision(route, method, pathname) {
  if (!route.steps || method === 'GET') return 'pass';
  if ((route.once ?? []).some(re => re.test(pathname))) return 'once';
  if (/^\/api\/auth\//.test(pathname)) return 'pass';
  if (method === 'POST' && READ_POSTS.some(re => re.test(pathname))) return 'pass';
  return 'stub';
}

/**
 * Antwort auf eine abgefangene schreibende Anfrage: der gesendete JSON-Körper zurück (PUT /api/kids/progress liefert den
 * Stand zurück — mit „{}" warf KidHub „e.levels is not iterable"), sonst „{}".
 */
export function stubBody(postData) {
  try { const v = JSON.parse(postData ?? ''); if (v && typeof v === 'object') return JSON.stringify(v); } catch { /* kein JSON */ }
  return '{}';
}

/** Katalog-Routen um die Zustände erweitert: je Zustand eine abgeleitete Route mit `steps`. */
export function withStates(routes, routeId) {
  const byId = new Map(routes.map(r => [routeId(r), r]));
  const out = [];
  for (const s of STATES) {
    const base = byId.get(s.of);
    if (!base) throw new Error(`states.mjs: Zustand „${s.name}" hängt an unbekannter Route ${s.of}`);
    const baseLabel = s.of.slice(base.app.length + 2);
    out.push({ ...base, label: `${baseLabel}--${s.name}`, state: s.name, stateOf: s.of, steps: s.steps,
      stateAuth: s.auth ?? null, fresh: !!s.fresh, once: s.once ?? [], expect: s.expect ?? null, main: s.main ?? null, foldExempt: s.main == null });
  }
  return out;
}

/**
 * Führt die Schritte aus. Gibt `null` zurück oder eine Fehlermeldung („Schritt 2 (click …): …").
 * `settle` wartet auf Netzruhe (wie nach dem Laden), damit Nachlade-Zustände (Lösung, Fehlermeldung) im Bild sind.
 */
export async function runSteps(page, steps, settle) {
  for (const [i, st] of steps.entries()) {
    const what = Object.entries(st).map(([k, v]) => `${k} ${v}`).join(', ');
    try {
      if (st.click) await page.locator(st.click).first().click({ timeout: 8000 });
      else if (st.clickIf) {
        const el = page.locator(st.clickIf).first();
        if (await el.isVisible().catch(() => false)) await el.click({ timeout: 8000 });
      }
      else if (st.select) await page.locator(st.select).first().selectOption(st.value, { timeout: 8000 });
      else if (st.fill) await page.locator(st.fill).first().fill(st.text ?? '', { timeout: 8000 });
      else if (st.press) await (st.sel ? page.locator(st.sel).first().press(st.press, { timeout: 8000 }) : page.keyboard.press(st.press));
      else if (st.hover) await page.locator(st.hover).first().hover({ timeout: 8000 });
      else if (st.blur) await page.locator(st.blur).first().evaluate(el => el.blur());
      else if (st.waitFor) await page.locator(st.waitFor).first().waitFor({ state: 'visible', timeout: 8000 });
      else if (st.wait) await page.waitForTimeout(st.wait);
      else if (st.move) await boardMove(page, st.move, st.board);
      else throw new Error('unbekannter Schritt');
      await settle();
      await page.waitForTimeout(250);
    } catch (e) {
      return `Schritt ${i + 1} (${what}): ${String(e.message).split('\n')[0].slice(0, 200)}`;
    }
  }
  return null;
}

/** Zug auf einem chessground-Brett: Feld anklicken, Zielfeld anklicken. Schwarz unten = `.orientation-black`. */
async function boardMove(page, uci, boardSel = 'cg-board') {
  const board = page.locator(boardSel).first();
  const box = await board.boundingBox({ timeout: 8000 });
  if (!box) throw new Error('Brett nicht sichtbar');
  const black = await board.evaluate(el => !!el.closest('.orientation-black'));
  for (const s of [uci.slice(0, 2), uci.slice(2, 4)]) {
    const p = squareCenter(box, s, black);
    await page.mouse.click(p.x, p.y);
    await page.waitForTimeout(150);
  }
}

/** Mitte eines Felds („e2") in Seitenkoordinaten; `black` = Brett von Schwarz aus gesehen (a1 oben rechts). */
export function squareCenter(box, square, black = false) {
  const f = square.charCodeAt(0) - 97; const r = Number(square[1]) - 1;
  if (!(f >= 0 && f < 8 && r >= 0 && r < 8)) throw new Error(`kein Feld: ${square}`);
  const col = black ? 7 - f : f; const row = black ? r : 7 - r;
  return { x: box.x + (col + 0.5) * box.width / 8, y: box.y + (row + 0.5) * box.height / 8 };
}

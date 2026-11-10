import { FORMAT_LOCALES, SUPPORTED_LANGS } from './core/locale.service';
import { SOLVER_ACTION_KEYS, SOLVER_EVAL_KEYS } from './features/puzzles/solver-actions.util';

/**
 * Sprachdateien-Parität (periodische Aufgabe „Übersetzungen prüfen", jetzt als Test):
 * en ist die Quelle. Die gepflegten Sprachen (FORMAT_LOCALES: de, hr) müssen exakt dieselben
 * Keys tragen — mit identischen {{Platzhaltern}} und ohne leere Werte. Alle SUPPORTED_LANGS
 * müssen valides JSON sein und dürfen keine Keys haben, die en nicht kennt (veraltete Reste
 * gelöschter Features). Karma serviert `public/` als Assets → fetch('/i18n/<lang>.json').
 */
type Flat = Record<string, string>;

function flatten(obj: unknown, prefix = '', out: Flat = {}): Flat {
  if (obj && typeof obj === 'object') {
    for (const [k, v] of Object.entries(obj as Record<string, unknown>)) {
      const key = prefix ? `${prefix}.${k}` : k;
      if (v && typeof v === 'object') flatten(v, key, out);
      else out[key] = String(v);
    }
  }
  return out;
}

async function load(lang: string): Promise<Flat> {
  // Angular-Karma serviert Assets je nach Version unter / oder /base/.
  for (const url of [`/i18n/${lang}.json`, `/base/i18n/${lang}.json`]) {
    const res = await fetch(url);
    if (res.ok) return flatten(await res.json());
  }
  throw new Error(`${lang}.json nicht ladbar`);
}

const placeholders = (s: string): string =>
  [...s.matchAll(/\{\{\s*([^}]+?)\s*\}\}/g)].map(m => m[1]).sort().join('|');

describe('i18n Sprachdateien', () => {
  let en: Flat;
  beforeAll(async () => { en = await load('en'); });

  /** Bewusst leere en-Werte: Suffixe, die es im Englischen nicht gibt. (Bis 0.739.0 `weekly.oClock` — „14:00 Uhr"; die
   *  Wochenpost zeigt seither die Uhrzeit im Format der Sprache.) */
  const INTENTIONALLY_EMPTY_EN = new Set<string>([]);

  it('en ist die Quelle: viele Keys, keine (unbeabsichtigt) leeren Werte', () => {
    expect(Object.keys(en).length).toBeGreaterThan(1000);
    const empty = Object.entries(en).filter(([k, v]) => !v.trim() && !INTENTIONALLY_EMPTY_EN.has(k)).map(([k]) => k);
    expect(empty).toEqual([]);
  });

  for (const lang of FORMAT_LOCALES.filter(l => l !== 'en')) {
    it(`${lang} hat exakt die Keys von en`, async () => {
      const l = await load(lang);
      const missing = Object.keys(en).filter(k => !(k in l));
      const extra = Object.keys(l).filter(k => !(k in en));
      expect(missing).withContext(`${lang}: fehlende Keys`).toEqual([]);
      expect(extra).withContext(`${lang}: Keys ohne en-Gegenstück`).toEqual([]);
    });

    it(`${lang}: Platzhalter wie in en, keine leeren Werte`, async () => {
      const l = await load(lang);
      const empty = Object.entries(l).filter(([, v]) => !v.trim()).map(([k]) => k);
      const phDiff = Object.keys(en).filter(k => k in l && placeholders(en[k]) !== placeholders(l[k]));
      expect(empty).withContext(`${lang}: leere Werte`).toEqual([]);
      expect(phDiff).withContext(`${lang}: {{Platzhalter}} weichen von en ab`).toEqual([]);
    });
  }

  // v1-rk-analysis-jobs-percent-wrap (DESIGN 5.13): „(100" / „%)" brach am Handy um — Zahl und % mit geschütztem Leerzeichen.
  // e1-rk-percent-space-en: das gilt für de/hr; Englisch und Ungarisch schreiben „46%" ohne Leerzeichen.
  const PCT_NO_SPACE = new Set(['en', 'hu']);
  for (const lang of FORMAT_LOCALES) {
    if (PCT_NO_SPACE.has(lang)) {
      it(`${lang}: Prozent ohne Leerzeichen („46%", „{{percent}}%")`, async () => {
        const l = await load(lang);
        const spaced = Object.entries(l).filter(([, v]) => /(\d|\}\})[   ]%/.test(v)).map(([k]) => k);
        expect(spaced).withContext(`${lang}: Leerzeichen vor %`).toEqual([]);
      });
    } else {
      it(`${lang}: zwischen Zahl-Platzhalter und % steht ein geschütztes Leerzeichen`, async () => {
        const l = await load(lang);
        const loose = Object.entries(l).filter(([, v]) => /\}\}( ?%)/.test(v)).map(([k]) => k);
        expect(loose).withContext(`${lang}: „}} %" mit normalem oder „}}%" ohne Leerzeichen`).toEqual([]);
      });
    }
  }

  /**
   * e1-rk-en-anfuehrungszeichen-gemischt (DESIGN 6): en.json mischte »…«, „…“, gerade "…" und “…”. Englisch zitiert
   * typografisch mit “…” (bzw. ‘…’). Ausgenommen sind gerade " als Syntax: HTML-Attribute (href="…") und PGN-Tags
   * ([Event "…"]) in Platzhaltern.
   */
  it('en: Anführungszeichen typografisch “…”, keine deutschen und keine geraden', () => {
    const syntax = (v: string) => v.replace(/[\w-]+="[^"]*"/g, '').replace(/\[\w+ "[^"]*"\]/g, '');
    const bad = Object.entries(en)
      .filter(([, v]) => /[»«„]/.test(v) || syntax(v).includes('"'))
      .map(([k, v]) => `${k} = ${v.slice(0, 80)}`);
    expect(bad).withContext('en: »…«, „…“ oder gerade "…" statt “…”').toEqual([]);
    const unbalanced = Object.entries(en).filter(([, v]) => (v.match(/“/g) ?? []).length !== (v.match(/”/g) ?? []).length)
      .map(([k]) => k);
    expect(unbalanced).withContext('en: “ ohne passendes ”').toEqual([]);
  });

  /** a1-rk-hilfe-guillemets (DESIGN 6): Deutsch zitiert typografisch „…“ — die Hilfe setzte Begriffe in »…«. */
  it('de: Anführungszeichen „…“, keine Guillemets »…«', async () => {
    const de = await load('de');
    const bad = Object.entries(de).filter(([, v]) => /[»«]/.test(v)).map(([k, v]) => `${k} = ${v.slice(0, 80)}`);
    expect(bad).withContext('de: »…« statt „…“').toEqual([]);
  });

  /**
   * a1-rk-datenschutz-ergaenzungsstrich (DESIGN 5.13): „Fortschritt und -Statistiken“ brach nach dem Strich um („und -“
   * am Zeilenende). Ergänzungsstrich nach „und/oder“ mit geschütztem Leerzeichen davor und Wortverbinder (U+2060) danach.
   */
  it('de: Ergänzungsstrich nach „und/oder“ bleibt am Wort', async () => {
    const de = await load('de');
    const bad = Object.entries(de).filter(([, v]) => /\b(?:und|oder|bzw\.|sowie)(?: -| -(?!⁠))\p{L}/u.test(v))
      .map(([k]) => k);
    expect(bad).withContext('de: „und -Wort“ ohne U+00A0/U+2060').toEqual([]);
    expect(de['legal.privacy.dataUsage']).toContain('und -⁠Statistiken');
  });

  /**
   * b1-w-privacy-strich-slash (DESIGN 5.13): WebKit brach „Chess.com-/Lichess-Benutzername“ zwischen Strich und
   * Schrägstrich — die neue Zeile begann mit „/Lichess…“. Ergänzungsstrich vor „/“ mit Wortverbinder (U+2060).
   */
  it('de/hu: „Wort-/Wort“ hält Strich und Schrägstrich zusammen (U+2060)', async () => {
    for (const lang of ['de', 'hu']) {
      const all = await load(lang);
      const bad = Object.entries(all).filter(([, v]) => /\p{L}-\/\p{L}/u.test(v)).map(([k]) => k);
      expect(bad).withContext(`${lang}: „-/“ ohne U+2060`).toEqual([]);
    }
    const de = await load('de');
    expect(de['legal.privacy.dataProfile']).toContain('Chess.com-\u2060/Lichess-Benutzername');
    expect(de['legal.privacy.dataUsage']).toContain('Endless-\u2060/Kurs-Fortschritt');
  });

  /**
   * d3-rk-partie-grund-strich-zeilenanfang (DESIGN 5.13): der Grund unter „Partie analysieren“ brach mit text-wrap:
   * balance genau vor dem Gedankenstrich um — die zweite Zeile begann mit „— versuch es …“. Kurze Gründe
   * (`*NoEngine`) tragen vor „—“ ein geschütztes Leerzeichen.
   */
  it('kurze Gründe (*NoEngine): vor „—“ ein geschütztes Leerzeichen, kein normales', async () => {
    for (const lang of FORMAT_LOCALES) {
      const all = lang === 'en' ? en : await load(lang);
      const bad = Object.entries(all).filter(([k, v]) => k.endsWith('NoEngine') && / [—–]/.test(v)).map(([k]) => k);
      expect(bad).withContext(`${lang}: „ —“ mit normalem Leerzeichen`).toEqual([]);
      expect(all['games.analyzeNoEngine']).toContain(' —');
    }
  });

  /**
   * d3-rk-punktepartie-benennung (DESIGN 2.9): auf Englisch hieß dasselbe Ziel „Point game“, „Points game“ und „Guess the
   * moves“. Menü, Seitentitel, Knopf auf Partie-Analysen, Startseiten-Kachel und Schnellstart tragen je Sprache denselben Namen.
   */
  it('Punktepartie heißt überall gleich (je gepflegter Sprache)', async () => {
    for (const lang of FORMAT_LOCALES) {
      const all = lang === 'en' ? en : await load(lang);
      const names = ['nav.guess', 'guess.title', 'gameAnalysis.toGuess', 'dashboard.guess.title', 'app.qs.guessTitle'].map(k => all[k]);
      expect(new Set(names).size).withContext(`${lang}: ${names.join(' | ')}`).toBe(1);
    }
  });

  /**
   * d3-rk-mistakes-filter-beschriftung-saison / d3-rk-edit-seite-liga-beschriftung: die Sammlung filtert nach den
   * Einordnungen „Plattform / Liga“ und „Modus / Jahrgang“ — Titel und Erklärsatz sprachen von „Liga/Saison“, und
   * „Seite / Liga“ las man neben „Meine Seite“ als Farbe.
   */
  it('de: Einordnungsfelder heißen „Plattform / Liga“ + „Modus / Jahrgang“, die Fehler-Sammlung nennt sie so', async () => {
    const de = await load('de');
    expect(de['games.classifier.first']).toBe('Plattform / Liga');
    expect(de['games.classifier.second']).toBe('Modus / Jahrgang');
    expect(de['games.mistakes.collection.hint']).toContain('Plattform/Liga');
    expect(de['games.mistakes.collection.hint']).toContain('Modus/Jahrgang');
    for (const k of ['games.mistakes.collection.title', 'games.mistakes.collection.button', 'games.mistakes.collection.hint'])
      expect(de[k]).not.toContain('Saison');
    expect(en['games.mistakes.collection.title']).not.toMatch(/league\/season/i);
  });

  /**
   * Englisch-Nachprüfung e1 (e1-rk-hilfe-partien-satz): die Hilfe übernahm die deutsche Satzstellung „Unter ☰ → … siehst
   * du …“ als „Under ☰ → Community → Leaderboards you see …“. Natürlich: „Your … are under ☰ → …“ / „The … under ☰ → …
   * show …“ — keine Ortsangabe am Satzanfang, an die ohne Komma „you …“ anschließt.
   */
  it('en: Hilfesätze ohne deutsche Satzstellung „Under ☰ → … you …“', () => {
    const bad = Object.entries(en).filter(([, v]) => /\b(?:Under|In) (?:☰|“)[^,.]*? you\b/.test(v))
      .map(([k, v]) => `${k} = ${v.slice(0, 80)}`);
    expect(bad).toEqual([]);
  });

  it('alle unterstützten Sprachen sind valides JSON ohne veraltete Keys', async () => {
    for (const lang of SUPPORTED_LANGS) {
      const l = await load(lang);
      const stale = Object.keys(l).filter(k => !(k in en));
      expect(stale).withContext(`${lang}: Keys, die en nicht kennt`).toEqual([]);
    }
  });
  /**
   * Codereview F2-012: Die Löse-Knöpfe (Zurücksetzen, Mausrutscher, Aufgeben, Bewertung an/aus) sind in
   * Standard, Endless und Buch dieselben. Endless hatte eigene Keys, die in de/hr nie übersetzt wurden
   * („Reset", „Mouseslip", „Give Up", „Show Eval"), das Buch eigene mit anderem Wort („Mausverrutscher").
   * Jeder Key, den ein Modus für diese Knöpfe holt, existiert und ist in den gepflegten Sprachen übersetzt.
   */
  for (const lang of FORMAT_LOCALES.filter(l => l !== 'en')) {
    it(`${lang}: Löse-Knöpfe aller drei Modi übersetzt`, async () => {
      const l = await load(lang);
      const keys = new Set<string>();
      for (const mode of ['standard', 'endless', 'book'] as const) {
        const a = SOLVER_ACTION_KEYS[mode];
        const e = SOLVER_EVAL_KEYS[mode];
        // start/now („Start“) dürfen wie en lauten — nur die Knopf-Beschriftungen müssen abweichen.
        [a.reset, a.mouseslip, a.giveUp, e.show, e.hide].forEach(k => keys.add(k));
        [e.start, e.now].forEach(k => expect(l[k]).withContext(`${lang}: ${k} fehlt`).toBeTruthy());
      }
      const untranslated = [...keys].filter(k => !l[k] || l[k] === en[k]).map(k => `${k} = „${l[k]}“`);
      expect(untranslated).withContext(`${lang}: Löse-Knöpfe fehlen oder stehen auf Englisch`).toEqual([]);
    });
  }

  /**
   * Codereview UX-006: Endless sprach in der deutschen und kroatischen Oberfläche Englisch („Endless Puzzle
   * Mode“, „Start Rating“, „Stockfish Depth“, „Themes (optional)“, „Game Over“, „Max Rating“). Kein
   * endless.*-Text einer gepflegten Sprache steht wortgleich wie en — außer den Einträgen unten, die in
   * de/hr/hu genauso lauten (Lehnwörter, Kürzel, reine Platzhalter).
   */
  const ENDLESS_SAME_AS_EN_OK = new Set([
    'endless.config.auto', 'endless.config.curvePuzzle', 'endless.config.highscore', 'endless.config.start',
    'endless.game.levelRange', 'endless.history.colEloDelta',
  ]);
  for (const lang of FORMAT_LOCALES.filter(l => l !== 'en')) {
    it(`${lang}: Endless-Texte übersetzt (nicht wortgleich wie en)`, async () => {
      const l = await load(lang);
      const english = Object.keys(en)
        .filter(k => k.startsWith('endless.') && !ENDLESS_SAME_AS_EN_OK.has(k) && l[k] === en[k])
        .map(k => `${k} = „${l[k]}“`);
      expect(english).withContext(`${lang}: endless.* steht auf Englisch`).toEqual([]);
    });
  }

  /**
   * Codereview F8-010: Gleiche Knopftexte standen je Feature neu (book.actions.next, worksheets.solve.next,
   * games.mistakes.next … neben common.next) und laufen dann auseinander — derselbe Mausrutscher-Knopf hieß
   * „Mausrutscher“, „Mouseslip“ und „Mausverrutscher“. Ein Key außerhalb von common.*, der in allen gepflegten
   * Sprachen wortgleich einen common.*-Text trägt, ist ein Doppel: stattdessen den common.*-Key benutzen.
   */
  it('kein Key außerhalb von common.* doppelt wortgleich einen common.*-Text', async () => {
    const all = [en, ...(await Promise.all(FORMAT_LOCALES.filter(l => l !== 'en').map(load)))];
    const common = Object.keys(en).filter(k => k.startsWith('common.'));
    const doubles = Object.keys(en)
      .filter(k => !k.startsWith('common.'))
      .map(k => [k, common.find(c => all.every(l => l[k] === l[c]))] as const)
      .filter(([, c]) => !!c)
      .map(([k, c]) => `${k} = ${c}`);
    expect(doubles).withContext('Doppel eines common.*-Texts — common.* verwenden').toEqual([]);
  });

  it('kennt zu jedem Schnellstart-Eintrag Titel und Beschreibung (alle gepflegten Sprachen)', async () => {
    // `quickstartItems` baut die i18n-Keys aus dem `key` zusammen (`app.qs.<key>Title|Desc`) —
    // ein neuer Eintrag ohne Texte fiele sonst erst im UI als roher Schlüssel auf.
    const keys = ['random', 'mate', 'endless', 'daily', 'weekly'];
    for (const lang of ['en', ...FORMAT_LOCALES.filter(l => l !== 'en')]) {
      const flat = await load(lang);
      for (const key of keys) {
        expect(flat[`app.qs.${key}Title`]).withContext(`${lang}: app.qs.${key}Title`).toBeTruthy();
        expect(flat[`app.qs.${key}Desc`]).withContext(`${lang}: app.qs.${key}Desc`).toBeTruthy();
      }
    }
  });

  /**
   * Rechtstexte (Codereview UX-022): Datenschutzerklaerung, Loeschseite und die Loesch-Karte im Profil (deren
   * Warnung vor der Passwort-Eingabe nennt seit UX-021 Kurse, Partien, Aufgabenblaetter usw., die Juni-Uebersetzungen
   * nur „Identitaet und persoenliche Daten“). Die gepflegten Sprachen tragen jeden Schluessel und denselben „Stand“
   * wie en. Die uebrigen Sprachen fallen per Schluessel-Luecke auf en zurueck und behalten nur Uebersetzungen von
   * en-Texten, die sich seit dem Uebersetzen nicht geaendert haben: bis 2026-10 stand dort „Stand: 3. Juni“ und zu
   * chess.com „keine automatische Datenuebertragung“, waehrend en den 6-Stunden-Abruf beschrieb. Aendert sich einer
   * der en-Texte unten, schlaegt der Test an — dann die Uebersetzungen dieses Schluessels in den uebrigen Sprachen
   * loeschen und den Eintrag streichen (es gilt en), oder alle neu uebersetzen und den Fingerabdruck nachziehen.
   */
  describe('Rechtstexte (legal.privacy, legal.accountDeletion, profile.delete)', () => {
    const isLegal = (k: string) =>
      k.startsWith('legal.privacy.') || k.startsWith('legal.accountDeletion.') || k.startsWith('profile.delete.');

    /** FNV-1a (32 bit) ueber den en-Text — der Stand, von dem aus die uebrigen Sprachen uebersetzt sind. */
    const fingerprint = (s: string): string => {
      let h = 0x811c9dc5;
      for (let i = 0; i < s.length; i++) h = Math.imul(h ^ s.charCodeAt(i), 0x01000193) >>> 0;
      return h.toString(16).padStart(8, '0');
    };

    /** Was die nicht gepflegten Sprachen uebersetzt haben duerfen, mit dem Fingerabdruck des en-Texts von damals. */
    const TRANSLATED_FROM_EN: Readonly<Record<string, string>> = {
      // 2026-11-09: alle 21 Teilsprachen komplett vom aktuellen en-Stand uebersetzt (i18n-Welle), deshalb jeder
      // Rechtstext-Schluessel mit dem heutigen Fingerabdruck; legal.privacy.updated tragen die Teilsprachen NICHT.
      'legal.privacy.title': '2526541f', 'legal.privacy.link': '37cb16ff',
      'legal.privacy.tocTitle': '871f67c3', 'legal.privacy.intro': '11947df0',
      'legal.privacy.introLeagueHub': '224174cf', 'legal.privacy.introClubHub': 'd18ff770',
      'legal.privacy.clubTitle': 'fcf9a7a9', 'legal.privacy.clubIntro': 'be1c669f',
      'legal.privacy.clubData': '799ac5b0', 'legal.privacy.clubContacts': 'b9f11796',
      'legal.privacy.clubAttendance': 'beb9357d', 'legal.privacy.clubAccess': '7e7b20b0',
      'legal.privacy.clubPhotos': '52884d4e', 'legal.privacy.clubLink': '213d90f5',
      'legal.privacy.clubRetention': '4b19fd84', 'legal.privacy.kidIntro': 'a830fd20',
      'legal.privacy.kidTitle': '6f142de8', 'legal.privacy.kidDevice': 'd46bd0f7',
      'legal.privacy.kidAccount': '66d4efc5', 'legal.privacy.kidParents': '17ed1b18',
      'legal.privacy.kidNoTracking': 'c15a13c1', 'legal.privacy.kidVisibility': 'c710b18a',
      'legal.privacy.kidLogs': '02ba787e', 'legal.privacy.kidPeriods': '1dd7b43d',
      'legal.privacy.kidDetails': '750fbd68', 'legal.privacy.controllerTitle': 'a15e8ea1',
      'legal.privacy.controllerNamed': '242bc89b', 'legal.privacy.leagueTitle': '6c0a5b22',
      'legal.privacy.leagueIntro': 'df661ed7', 'legal.privacy.leagueClub': 'd8a4f6e5',
      'legal.privacy.leagueSourcesTitle': '2afb7af1', 'legal.privacy.leagueSourcesPairings': '1967b03f',
      'legal.privacy.leagueSourcesGames': '99e526ea', 'legal.privacy.leagueSourcesClub': 'b7ff3226',
      'legal.privacy.leagueSourcesOnline': 'ce6335cd', 'legal.privacy.leagueData': 'b44bba20',
      'legal.privacy.leagueBasis': '836ad8bc', 'legal.privacy.leagueRecipients': 'c8cd90b1',
      'legal.privacy.leagueRetention': 'e646947c', 'legal.privacy.leagueObjection': '2f7f4b90',
      'legal.privacy.dataTitle': 'df172352', 'legal.privacy.dataIntro': 'f6c135d3',
      'legal.privacy.dataAccount': '07a9ced8', 'legal.privacy.dataProfile': 'acf57914',
      'legal.privacy.dataUsage': 'e3661c7e', 'legal.privacy.dataTechnical': '16aa52a9',
      'legal.privacy.dataIpCountry': 'c49e5903', 'legal.privacy.dataScoresheet': '863dbb6a',
      'legal.privacy.purposesTitle': '8f067ecb', 'legal.privacy.purposes': '74d5163f',
      'legal.privacy.visibilityTitle': '919aaca2', 'legal.privacy.visibilitySearch': '45ab9a26',
      'legal.privacy.visibilityLeaderboards': 'c2aec61a', 'legal.privacy.visibilityFriends': '42334cde',
      'legal.privacy.visibilityGuests': 'f9cb5555', 'legal.privacy.messagesTitle': '18609bcb',
      'legal.privacy.messages': 'f3778af4', 'legal.privacy.pushTitle': 'aa4b62d1',
      'legal.privacy.push': 'bd17bd0f', 'legal.privacy.thirdTitle': '0883fee4',
      'legal.privacy.thirdIntro': '66056906', 'legal.privacy.thirdDiscord': 'd11a95bf',
      'legal.privacy.thirdChessresults': 'c7a5e460', 'legal.privacy.thirdChesstools': 'ff419e7e',
      'legal.privacy.thirdChesssites': '4b700680', 'legal.privacy.thirdLogging': '1c02fd53',
      'legal.privacy.thirdPush': 'cc6bd48f', 'legal.privacy.thirdHosting': '6325d96e',
      'legal.privacy.thirdTextLlm': '2f09a2f3',
      'legal.privacy.aiTitle': 'e8a69c8d',
      'legal.privacy.botTitle': '97fe8cd6',
      'legal.privacy.botIntro': '99df6b20', 'legal.privacy.botDmLog': 'b3f44bb8',
      'legal.privacy.botActivity': '842d8710', 'legal.privacy.botLogs': '89d55f3a',
      'legal.privacy.tournamentTitle': '19a17c25', 'legal.privacy.tournamentIntro': 'f42c5055',
      'legal.privacy.tournamentData': '5cb6f865', 'legal.privacy.tournamentRetention': '9573eb81',
      'legal.privacy.logsTitle': '36c80d5d', 'legal.privacy.logsIntro': '7815fa26',
      'legal.privacy.logsContent': 'a7ccf82b', 'legal.privacy.logsEmail': 'adf1ee51',
      'legal.privacy.logsAccess': 'df454be2', 'legal.privacy.logsRetention': '23e15c1b',
      'legal.privacy.storageTitle': 'c9258e8d', 'legal.privacy.storage': 'ca3d4776',
      'legal.privacy.storageLogout': '3ed0e071', 'legal.privacy.retentionTitle': 'f2846ea7',
      'legal.privacy.retention': '731855af', 'legal.privacy.retentionLink': '3220e42f',
      'legal.privacy.periodsIntro': '1b051f58', 'legal.privacy.periodsColData': '3f5279c5',
      'legal.privacy.periodsColPeriod': '87248424', 'legal.privacy.periodsGroupAccount': '95fd980f',
      'legal.privacy.periodsGroupAnon': 'f85c1aa9', 'legal.privacy.periodsGroupOther': '58581c58',
      'legal.privacy.periods.account.data': '602ffedb', 'legal.privacy.periods.account.period': 'b5b3acbb',
      'legal.privacy.periods.notifications.data': '2f1e9009', 'legal.privacy.periods.notifications.period': 'e09dabbf',
      'legal.privacy.periods.messages.data': 'ff45a740', 'legal.privacy.periods.messages.period': 'b2892ff5',
      'legal.privacy.periods.scoresheetFailed.data': '1d884a32', 'legal.privacy.periods.scoresheetFailed.period': '7a572af9',
      'legal.privacy.periods.scoresheetSaved.data': '49a256c2', 'legal.privacy.periods.scoresheetSaved.period': '02fbb9d6',
      'legal.privacy.periods.scoresheetArchive.data': 'be545519', 'legal.privacy.periods.scoresheetArchive.period': '4a1b0b6e',
      'legal.privacy.periods.sharedLogin.data': '2e7e883d', 'legal.privacy.periods.sharedLogin.period': '7a572af9',
      'legal.privacy.periods.linkCodes.data': 'c4fbf630', 'legal.privacy.periods.linkCodes.period': '05080cfb',
      'legal.privacy.periods.endless.data': '9239a72b', 'legal.privacy.periods.endless.period': '1a856dd9',
      'legal.privacy.periods.attempts.data': 'd4903d86', 'legal.privacy.periods.attempts.period': '2d8a9817',
      'legal.privacy.periods.extension.data': '408fa4c3', 'legal.privacy.periods.extension.period': 'db0eb8e3',
      'legal.privacy.periods.leagueDrafts.data': '7ccbbbc4', 'legal.privacy.periods.leagueDrafts.period': 'af5443b5',
      'legal.privacy.periods.scoresheetAnon.data': '5bcad89c', 'legal.privacy.periods.scoresheetAnon.period': '9f7b3a13',
      'legal.privacy.periods.ipHash.data': '37468602', 'legal.privacy.periods.ipHash.period': '5c3e62e8',
      'legal.privacy.periods.ebooks.data': '393a384d', 'legal.privacy.periods.ebooks.period': '7a572af9',
      'legal.privacy.periods.logs.data': '36c80d5d', 'legal.privacy.periods.logs.period': '2d8a9817',
      'legal.privacy.periods.backups.data': '30533d2f', 'legal.privacy.periods.backups.period': '9f7b3a13',
      'legal.privacy.periods.botDm.data': 'b9df7a1a', 'legal.privacy.periods.botDm.period': '7a572af9',
      'legal.privacy.rightsTitle': 'cccb11a5', 'legal.privacy.rights': '76ad9910',
      'legal.privacy.contactTitle': '73ac94c3', 'legal.privacy.contact': '36e2a115',
      'legal.privacy.back': '78247cd6', 'legal.privacy.introTurnier': '89ed1931',
      'legal.accountDeletion.title': '76a85421', 'legal.accountDeletion.intro': '815ca664',
      'legal.accountDeletion.introPartner': '36cf5a6f', 'legal.accountDeletion.inAppTitle': '3220e42f',
      'legal.accountDeletion.inApp': '7a81abf7', 'legal.accountDeletion.inPartner': '0e4430f5',
      'legal.accountDeletion.deleteNow': 'd22ea705', 'legal.accountDeletion.deleteOnRookHub': '97b78c6e',
      'legal.accountDeletion.backupTitle': 'fb8c1315', 'legal.accountDeletion.backup': '97cabfd2',
      'legal.accountDeletion.removedTitle': '835d70fd', 'legal.accountDeletion.removed1': '2d56c130',
      'legal.accountDeletion.removedCourses': '5818c5f7', 'legal.accountDeletion.removed2': 'f583b311',
      'legal.accountDeletion.removedGames': '327e4077', 'legal.accountDeletion.removedShared': '7ad9db76',
      'legal.accountDeletion.removedKids': 'da6e53cb', 'legal.accountDeletion.removedConnections': '6f31a6f7',
      'legal.accountDeletion.removedLeague': 'e9febf67', 'legal.accountDeletion.keptTitle': '5398635d',
      'legal.accountDeletion.kept': 'aef76a8e', 'legal.accountDeletion.keptLeagueTitle': '2d43bdec',
      'legal.accountDeletion.keptLeague': '30fda58b', 'legal.accountDeletion.leagueGamesLink': 'c103063f',
      'legal.accountDeletion.keptLeagueShares': 'fa738292', 'legal.accountDeletion.contactTitle': '73ac94c3',
      'legal.accountDeletion.contact': '2d120dae', 'legal.accountDeletion.back': '78247cd6',
      'profile.delete.title': '76a85421', 'profile.delete.hint': 'a5ce1ca7',
      'profile.delete.button': '76a85421', 'profile.delete.warn': 'bf16a9b0',
      'profile.delete.password': '2cc30838', 'profile.delete.confirm': 'd8100383',
      'profile.delete.deleting': '6476ae31', 'profile.delete.done': '24eca02e',
      'profile.delete.wrongPassword': '3024a25f', 'profile.delete.failed': '7639131d',
      'profile.delete.moreInfo': '8d527148',
    };

    /** Monatsnamen (Wortstaemme, klein) der gepflegten Sprachen — hr im Genitiv („30. rujna“). */
    const MONTHS: Readonly<Record<string, readonly string[]>> = {
      en: ['january', 'february', 'march', 'april', 'may', 'june', 'july', 'august', 'september', 'october', 'november', 'december'],
      de: ['januar', 'februar', 'märz', 'april', 'mai', 'juni', 'juli', 'august', 'september', 'oktober', 'november', 'dezember'],
      hr: ['siječ', 'veljač', 'ožuj', 'trav', 'svib', 'lip', 'srp', 'kolovoz', 'ruj', 'listopad', 'studen', 'prosin'],
      hu: ['január', 'február', 'március', 'április', 'május', 'június', 'július', 'augusztus', 'szeptember', 'október', 'november', 'december'],
    };
    /** „Stand: 30. September 2026“ → „2026-9-30“; Tag, Monat oder Jahr nicht erkannt → NaN/0 im Ergebnis. */
    const legalDate = (lang: string, text: string): string => {
      const nums = (text.match(/\d+/g) ?? []).map(Number);
      const lower = text.toLowerCase();
      const month = (MONTHS[lang] ?? []).findIndex(m => lower.includes(m)) + 1;
      return `${nums.find(n => n > 1000)}-${month}-${nums.find(n => n >= 1 && n <= 31)}`;
    };

    it('die en-Texte, von denen aus uebersetzt wurde, sind unveraendert', () => {
      const changed = Object.entries(TRANSLATED_FROM_EN)
        .filter(([k, fp]) => !(k in en) || fingerprint(en[k]) !== fp)
        .map(([k]) => `${k} (jetzt ${k in en ? fingerprint(en[k]) : 'geloescht'})`);
      expect(changed).withContext('en geaendert: Uebersetzungen loeschen oder neu uebersetzen').toEqual([]);
    });

    for (const lang of FORMAT_LOCALES.filter(l => l !== 'en')) {
      it(`${lang}: jeder Rechtstext-Schluessel von en, derselbe Stand`, async () => {
        const l = await load(lang);
        const missing = Object.keys(en).filter(k => isLegal(k) && !(k in l));
        expect(missing).withContext(`${lang}: fehlende Rechtstexte`).toEqual([]);
        expect(MONTHS[lang]).withContext(`${lang}: Monatsnamen fuer den Stand fehlen`).toBeDefined();
        expect(legalDate(lang, l['legal.privacy.updated'] ?? ''))
          .withContext(`${lang}: legal.privacy.updated „${l['legal.privacy.updated']}“`)
          .toBe(legalDate('en', en['legal.privacy.updated']));
      });
    }

    it('die uebrigen Sprachen tragen keinen eigenen Stand und keine veralteten Rechtstexte', async () => {
      expect(legalDate('en', en['legal.privacy.updated'])).toMatch(/^\d{4}-([1-9]|1[0-2])-\d{1,2}$/);
      for (const lang of SUPPORTED_LANGS.filter(l => !FORMAT_LOCALES.includes(l))) {
        const l = await load(lang);
        const stale = Object.keys(l).filter(k => isLegal(k) && !(k in TRANSLATED_FROM_EN));
        expect(stale).withContext(`${lang}: Rechtstexte ohne aktuellen en-Stand (loeschen, dann gilt en)`).toEqual([]);
      }
    });
  });

});

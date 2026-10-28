// Parameter für die Routen mit Platzhaltern ({gameId}, {bookId} …) — über die API gesucht, und wo das Konto
// noch nichts hat, angelegt. Angelegtes trägt „ui-sweep" im Namen und wird beim nächsten Lauf wiedergefunden,
// es wächst also nichts nach (Ausnahme: der LeagueHub-Teilen-Link, Teilen-Links lassen sich nicht auflisten).
//
// Jeder Resolver ist für sich: scheitert einer, fehlt nur sein Parameter, und die betroffenen Routen werden
// übersprungen (im Bericht unter „nicht auflösbar").

import { PNG } from 'pngjs';

const SWEEP_TAG = 'ui-sweep';

/** Geteilte Linie (/l/{token}) — eine Zugfolge, wie sie die Repertoire-Seite beim „Linie teilen" schickt. */
const LINE_PGN = `[Event "${SWEEP_TAG}"]\n[Result "*"]\n\n1. e4 e5 2. Nf3 Nc6 3. Bb5 a6 4. Ba4 Nf6 5. O-O Be7 *\n`;

/** Eigener Kalkulationskurs des normalen Kontos (die Kalkulations-Serie auf Dev ist privat). */
const CALC_COURSE = `Kalkulation (${SWEEP_TAG})`;
const CALC_CHAPTER = 'Woche 1';
const CALC_POSITIONS = [
  'r1bq1rk1/pppp1ppp/2n2n2/2b1p3/2B1P3/2NP1N2/PPP2PPP/R1BQK2R w KQ - 1 6 Weiß am Zug: rechne Ng5 durch',
  '2r3k1/pp3ppp/2n5/3p4/3P4/2PB4/P4PPP/4R1K1 w - - 0 1 Weiß am Zug',
  'r4rk1/1pp1qppp/p1np1n2/2b1p1B1/2B1P1b1/P1NP1N2/1PP1QPPP/R4RK1 w - - 0 10 Was passiert nach Nd5?',
].join('\n');

/** Zugvergleich (/analysis/compare/{id}): Italienisch nach 3…Lc5, drei Kandidaten. */
const BLACK_PGN = `[Event "${SWEEP_TAG}"]\n[Result "*"]\n\n1. e4 c6 2. d4 d5 3. e5 Bf5 4. Nf3 e6 5. Be2 c5 *\n\n[Event "${SWEEP_TAG}"]\n[Result "*"]\n\n1. e4 c6 2. d4 d5 3. Nc3 dxe4 4. Nxe4 Bf5 5. Ng3 Bg6 *\n`;
// Endspiel-Repertoire: Stellung per FEN (Lucena-artig vereinfacht: König + Bauer gegen König, Opposition).
const ENDGAME_PGN = `[Event "${SWEEP_TAG}"]\n[SetUp "1"]\n[FEN "4k3/8/4K3/4P3/8/8/8/8 w - - 0 1"]\n[Result "*"]\n\n1. Kd6 Kd8 2. e6 Ke8 3. e7 Kf7 4. Kd7 *\n`;
const COMPARE_FEN = 'r1bqk1nr/pppp1ppp/2n5/2b1p3/2B1P3/5N2/PPPP1PPP/RNBQK2R w KQkq - 4 4';

const SAMPLE_PGN = `[Event "${SWEEP_TAG}"]
[White "Sweep, Weiss"]
[Black "Sweep, Schwarz"]
[Result "1-0"]
[Date "2026.10.10"]

1. e4 e5 2. Nf3 Nc6 3. Bb5 a6 4. Ba4 Nf6 5. O-O Be7 6. Re1 b5 7. Bb3 d6 8. c3 O-O
9. h3 Nb8 10. d4 Nbd7 11. c4 c6 12. cxb5 axb5 13. Nc3 Bb7 14. Bg5 b4 15. Nb1 h6
16. Bh4 c5 17. dxe5 Nxe4 18. Bxe7 Qxe7 19. exd6 Qf6 20. Nbd2 Nxd6 21. Nc4 Nxc4
22. Bxc4 Nb6 23. Ne5 Rae8 24. Bxf7+ Rxf7 25. Nxf7 Rxe1+ 26. Qxe1 Kxf7 27. Qe3 Qg5
28. Qxg5 hxg5 29. b3 Ke6 30. a3 Kd6 31. axb4 cxb4 32. Ra5 Nd5 33. f3 Bc8 34. Kf2 Bf5
35. Ra7 g6 36. Ra6+ Kc5 37. Ke1 Nf4 38. g3 Nxh3 39. Kd2 Kb5 40. Rd6 Kc5 41. Ra6 Nf2
42. g4 Bd3 43. Re6 1-0
`;

export async function login(base, user, password) {
  for (let attempt = 1; ; attempt++) {
    const res = await fetch(`${base}/api/auth/login`, {
      method: 'POST', headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ username: user, password }),
    });
    // Die Anmeldung hat ein eigenes, enges Rate-Limit — mit vier Konten und zwei Läufen kurz hintereinander ist es voll.
    if (res.status === 429 && attempt < 4) {
      console.log(`  Anmeldung ${user}: 429 (Rate-Limit) — eine Minute warten …`);
      await new Promise(r => setTimeout(r, 61000));
      continue;
    }
    if (!res.ok) throw new Error(`Anmeldung an ${base} gescheitert: HTTP ${res.status}`);
    return res.json();   // AuthResponse — genau das legt die App als `rookhub_user` in den localStorage
  }
}

export function api(base, token) {
  const call = async (method, path, body, retried = false) => {
    // Drossel der API (100/min je Adresse): 0,65 s Abstand; ein 429 trotzdem (das Fenster des Servers zählt auch die
    // Anfragen davor) → eine Minute warten und einmal neu — sonst fehlten ganze Parameter.
    await new Promise(r => setTimeout(r, 650));
    const form = body instanceof FormData;
    const res = await fetch(`${base}${path}`, {
      method,
      headers: { ...(token ? { Authorization: `Bearer ${token}` } : {}), ...(body !== undefined && !form ? { 'content-type': 'application/json' } : {}) },
      body: body === undefined ? undefined : form ? body : JSON.stringify(body),
    });
    if (res.status === 429 && !retried) {
      console.log(`  ${method} ${path}: 429 (Rate-Limit) — eine Minute warten …`);
      await new Promise(r => setTimeout(r, 61000));
      return call(method, path, body, true);
    }
    if (!res.ok) throw new Error(`${method} ${path} → HTTP ${res.status} ${(await res.text()).slice(0, 160)}`);
    const text = await res.text();
    return text ? JSON.parse(text) : null;
  };
  return { get: p => call('GET', p), post: (p, b) => call('POST', p, b ?? {}), put: (p, b) => call('PUT', p, b ?? {}) };
}

const list = x => Array.isArray(x) ? x : (x?.items ?? []);

/** Sucht/legt alle Parameter an. `log(name, wert | Fehler | null, grund?)` meldet jeden Resolver. Mit `only` (Menge von
 *  Namen) laufen nur die Resolver, die mindestens einen davon liefern — für das normale Konto die konto-eigenen.
 *  `viewers` (Rolle → Token, `anon` = null): mit diesen Ausweisen prüfen Resolver geteilter Inhalte (E-Books), dass
 *  wirklich jede Rolle des Laufs den Parameter sieht. */
export async function resolveParams(base, token, log = () => {}, previous = {}, only = null, viewers = {}) {
  const a = api(base, token);
  const p = {};
  const today = new Date();
  // UTC-Datum: der Server kennt das Tagespuzzle nur für heute/gestern in UTC — kurz nach Mitternacht in Wien wäre das Ortsdatum schon morgen (400).
  p.today = `${today.getUTCFullYear()}${String(today.getUTCMonth() + 1).padStart(2, '0')}${String(today.getUTCDate()).padStart(2, '0')}`;

  // Ein Resolver gibt { name: wert, …, _why?: 'Grund' } zurück — `_why` erklärt in der Ausgabe, warum ein Wert fehlt.
  const step = async (names, fn) => {
    if (only && !names.some(n => only.has(n))) return;
    try {
      const out = await fn();
      for (const n of names) {
        if (out?.[n] !== undefined && out[n] !== null && out[n] !== '') { p[n] = String(out[n]); log(n, p[n]); }
        else log(n, null, out?._why);
      }
    } catch (e) { for (const n of names) log(n, e); }
  };

  // Rechnet auf Dev eine Engine? Ohne eine bleiben Partie-Analysen und Zugvergleiche aus (Dev hatte am 11.10. keine).
  let engine = null;
  try { engine = await a.get('/api/game-analyses/guess/status'); } catch { /* unbekannt */ }
  const noEngine = engine && !engine.engineAvailable
    ? 'keine Engine auf Dev (weder eigene noch Haus-Engine)' : undefined;

  // Eigene Partie (angelegt per PGN-Import) + Teilen-Link; eine schon analysierte eigene Partie, wenn es eine gibt.
  await step(['gameId', 'gameShareToken', 'analyzedGameId', 'analyzedShareToken'], async () => {
    let games = list(await a.get('/api/games?take=200'));
    if (!games.some(g => g.white === 'Sweep, Weiss')) {
      await a.post('/api/games/import', { pgn: SAMPLE_PGN });
      games = list(await a.get('/api/games?take=200'));
    }
    const seed = games.find(g => g.white === 'Sweep, Weiss');
    const analyzed = games.find(g => g.analysis?.status === 'done');
    let why;
    if (seed && !analyzed && (!seed.analysis || seed.analysis.status === 'failed')) {
      // Einmal anstoßen — fertig ist sie erst nach Minuten, also beim NÄCHSTEN Lauf. Idempotent: die API hängt eine
      // vorhandene (nicht gescheiterte) Analyse derselben Partie wieder an, statt neu zu rechnen.
      if (noEngine) why = `${noEngine} — wird angestoßen, sobald eine rechnet`;
      else {
        try {
          const r = await a.post(`/api/games/${seed.id}/analyze`, {});
          why = r?.reason ? `Analyse abgelehnt: ${r.reason}` : 'Analyse angestoßen — fertig beim nächsten Lauf';
        } catch (e) { why = `Analyse abgelehnt: ${e.message}`; }
      }
    } else if (seed?.analysis && !analyzed) why = `Analyse läuft noch (${seed.analysis.status})`;
    const share = async g => g ? (await a.get(`/api/games/${g.id}`)).shareToken : undefined;
    return {
      gameId: seed?.id, gameShareToken: await share(seed),
      analyzedGameId: analyzed?.id, analyzedShareToken: await share(analyzed), _why: why,
    };
  });

  await step(['repertoireId'], async () => ({ repertoireId: list(await a.get('/api/repertoires'))[0]?.id }));
  // Repertoire-Varianten: ein Schwarz-Repertoire (Brett gedreht) und ein Endspiel-Repertoire — seed-user.mjs legt beide an.
  // Das Admin-Konto bekommt beide hier selbst (kleine PGNs, „ui-sweep" im Namen) — sonst fehlten 4 Routen beim Admin.
  await step(['repertoireBlackId', 'repertoireEndgameId'], async () => {
    let reps = list(await a.get('/api/repertoires'));
    const black = reps.find(r => r.kind === 'Opening' && /schwarz|black/i.test(r.name));
    const endgame = reps.find(r => r.kind === 'Endgame');
    if ((!black || !endgame) && only === null) {
      for (const [name, kind, pgn] of [[`Schwarz: Caro-Kann (${SWEEP_TAG})`, 'Opening', BLACK_PGN], [`Endspiel (${SWEEP_TAG})`, 'Endgame', ENDGAME_PGN]]) {
        if (kind === 'Opening' ? black : endgame) continue;
        try {
          const rep = await a.post('/api/repertoires', { name, description: '', isPublic: false, kind });
          const fd = new FormData();
          fd.append('file', new Blob([pgn], { type: 'application/x-chess-pgn' }), `${SWEEP_TAG}.pgn`);
          await a.post(`/api/repertoires/${rep.id}/files`, fd);
        } catch { /* anlegen gescheitert: Route bleibt „nicht auflösbar" */ }
      }
      reps = list(await a.get('/api/repertoires'));
    }
    return {
      repertoireBlackId: reps.find(r => r.kind === 'Opening' && /schwarz|black/i.test(r.name))?.id,
      repertoireEndgameId: reps.find(r => r.kind === 'Endgame')?.id,
    };
  });

  // Geteilte Linie (/l/{token}): eine Zugfolge aus einem EIGENEN Repertoire teilen. Dieselbe Linie erneut geteilt gibt
  // denselben Link (die API entdoppelt je Besitzer) — es wächst also nichts nach.
  await step(['lineToken'], async () => {
    for (const rep of list(await a.get('/api/repertoires')).slice(0, 5)) {
      try {
        const r = await a.post(`/api/repertoires/${rep.id}/share-line`, { pgn: LINE_PGN, title: SWEEP_TAG });
        if (r?.shareToken) return { lineToken: r.shareToken };
      } catch { /* fremdes/geteiltes Repertoire: nur der Besitzer darf teilen — das nächste versuchen */ }
    }
    return { _why: 'kein eigenes Repertoire' };
  });

  await step(['worksheetId', 'worksheetToken'], async () => {
    let ws = list(await a.get('/api/worksheets')).find(w => w.name === SWEEP_TAG);
    if (!ws) {
      ws = await a.post('/api/worksheets', { name: SWEEP_TAG, perPage: 6 });
      await a.post('/api/worksheets/items', {
        worksheetId: ws.id,
        items: [
          { fen: 'r1bqkbnr/pppp1ppp/2n5/4p2Q/2B1P3/8/PPPP1PPP/RNB1K1NR w KQkq - 2 4', heading: 'Matt in 1', text: 'Weiß am Zug.', solutionMoves: 'h5f7' },
          { fen: '6k1/5ppp/8/8/8/8/5PPP/3R2K1 w - - 0 1', heading: 'Grundreihe', solutionMoves: 'd1d8' },
          { fen: 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1', heading: 'Freie Stellung' },
        ],
      });
    }
    const token = ws.shareToken ?? (await a.post(`/api/worksheets/${ws.id}/share`)).shareToken;
    return { worksheetId: ws.id, worksheetToken: token };
  });

  // E-Books (0.764.0 ff.): beide Leser-Routen laufen in ALLEN Rollen, auch abgemeldet — die Bücher müssen also für
  // „Everyone" freigegeben sein (abgemeldet sieht man genau diese, EbookAccess.Visible). Sonst sah nur der Admin sie
  // (alles verwalten) und die anderen Rollen fotografierten 404. Ob jede Rolle das Buch wirklich sieht, prüft
  // `visibleToAll` mit den Ausweisen aus `viewers` (Rolle → Token, anon = null).
  const ebooks = ebookHelpers(base, a, viewers);

  // Leser: ein winziges PDF mit dem Sweep-Namen — hochgeladen, wenn es fehlt (braucht Ebooks__Root auf Dev).
  await step(['ebookId'], async () => {
    let book = list(await a.get(`/api/ebooks?search=${encodeURIComponent(SWEEP_TAG)}`)).find(b => b.title === SWEEP_TAG);
    if (!book) {
      if (only !== null) return { _why: 'nur über das Admin-Konto' };
      const pdf = '%PDF-1.4\n1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj\n2 0 obj<</Type/Pages/Kids[3 0 R]/Count 1>>endobj\n'
        + '3 0 obj<</Type/Page/Parent 2 0 R/MediaBox[0 0 200 300]>>endobj\nxref\n0 4\n0000000000 65535 f \n'
        + '0000000009 00000 n \n0000000052 00000 n \n0000000101 00000 n \ntrailer<</Size 4/Root 1 0 R>>\nstartxref\n166\n%%EOF\n';
      const fd = new FormData();
      fd.append('file', new Blob([pdf], { type: 'application/pdf' }), `${SWEEP_TAG}.pdf`);
      fd.append('title', SWEEP_TAG);
      fd.append('tags', 'ui-sweep');
      try { book = await a.post('/api/ebooks', fd); }
      catch (e) { return { _why: `Hochladen gescheitert (Ebooks__Root auf Dev gesetzt?): ${String(e).slice(0, 120)}` }; }
    }
    return await ebooks.shareWithAll(book, only) ? { ebookId: book.id } : { _why: `Buch ${book.id} nicht für alle Rollen sichtbar` };
  });

  // Leser mit erkannten Diagrammen (Brett rechts): ein PDF-Buch (EPUB-Leser hängt an Kapiteldateien — auf Dev fehlten
  // dort Einträge, 404 je Kapitel) mit fertig erkannten Diagrammen, dessen Datei sich laden lässt. Reihenfolge: das vom
  // letzten Lauf (stabile Bilder), ein schon für alle freigegebenes, sonst das kürzeste — das gibt der Admin frei.
  await step(['ebookDiagramId'], async () => {
    const all = list(await a.get('/api/ebooks?withDiagrams=true&pageSize=100'))
      .filter(b => b.diagramState === 'done' && (b.diagramCount ?? 0) > 0 && b.primaryFormat === 'pdf');
    const prev = all.find(b => String(b.id) === String(previous.ebookDiagramId));
    const shared = await ebooks.everyoneId().then(id => all.filter(b => (b.groupIds ?? []).includes(id)));
    const rest = [...all].sort((x, y) => (x.pageCount ?? 1e9) - (y.pageCount ?? 1e9));
    for (const book of [prev, ...shared, ...rest].filter(Boolean)) {
      if (!await ebooks.fileLoads(book.id)) continue;
      if (await ebooks.shareWithAll(book, only)) return { ebookDiagramId: book.id };
      if (only !== null) break;
    }
    return { _why: 'kein PDF-Buch mit erkannten Diagrammen, das alle Rollen sehen (Freigabe „Everyone" nur über das Admin-Konto)' };
  });

  await step(['reconstructionId', 'reconstructionToken'], async () => {
    let rc = list(await a.get('/api/reconstructions')).find(x => x.title === SWEEP_TAG);
    if (!rc) {
      rc = await a.post('/api/reconstructions', { title: SWEEP_TAG, white: 'Sweep, Weiss', black: 'Sweep, Schwarz' });
      await a.post(`/api/reconstructions/${rc.id}/parts`, { kind: 0, moves: 'e4 e5 Nf3 Nc6 Bb5 a6', continuesPrevious: true });
      await a.post(`/api/reconstructions/${rc.id}/parts`, { kind: 1, fen: 'r1bqkb1r/1ppp1ppp/p1n2n2/4p3/B3P3/5N2/PPPP1PPP/RNBQK2R w KQkq - 2 5' });
    }
    const detail = await a.get(`/api/reconstructions/${rc.id}`);
    const token = detail.shareToken ?? (await a.post(`/api/reconstructions/${rc.id}/share`)).shareToken;
    return { reconstructionId: rc.id, reconstructionToken: token };
  });

  await step(['bookId', 'calcBookId', 'bookPuzzleId', 'chapterIndex'], async () => {
    const courses = list(await a.get('/api/courses'));
    const book = courses.find(c => !c.isCalculation && (c.puzzleCount ?? 0) > 20) ?? courses.find(c => !c.isCalculation);
    // Der Admin sieht ALLE Kurse, auch den eigenen Kalkulationskurs des normalen Kontos — den nicht nehmen.
    let calc = courses.find(c => c.isCalculation && (c.isOwned || c.displayName !== CALC_COURSE));
    // Kein Kalkulationskurs sichtbar (das normale Konto): einen EIGENEN anlegen — leerer persönlicher Kurs, eine Woche
    // Stellungen, Kalkulations-Modus an. Wiedergefunden über den Namen.
    if (!calc) {
      let own = courses.find(c => c.isOwned && c.displayName === CALC_COURSE);
      if (!own) {
        const fd = new FormData();
        fd.append('name', CALC_COURSE);
        own = await a.post('/api/courses', fd);
      }
      const lines = await a.get(`/api/courses/${own.bookId}/lines?chapter=${encodeURIComponent(CALC_CHAPTER)}`);
      if (!list(lines).length) await a.post(`/api/courses/${own.bookId}/lines`, { chapter: CALC_CHAPTER, text: CALC_POSITIONS });
      await a.put(`/api/courses/${own.bookId}/calculation`, { isCalculation: true });
      calc = own;
    }
    let bookPuzzleId, chapterIndex;
    if (book) {
      const next = await a.get(`/api/courses/${book.bookId}/next?mode=sequential`);
      bookPuzzleId = next?.puzzle?.id ?? next?.id;
      // Kapitelansicht (/courses/{bookId}/chapter/{i}/…): das erste Kapitel mit Aufgaben.
      const chapters = list(await a.get(`/api/courses/${book.bookId}/chapters`));
      chapterIndex = (chapters.find(c => (c.puzzleCount ?? 0) > 0) ?? chapters[0])?.index;
    }
    return { bookId: book?.bookId, calcBookId: calc?.bookId, bookPuzzleId, chapterIndex };
  });
  // Woodpecker-Plan (0.773.0) über den Aufgabenkurs — angelegt, wenn das Konto noch keinen hat.
  await step(['woodpeckerPlanId'], async () => {
    let plan = list(await a.get('/api/woodpecker'))[0];
    if (!plan && p.bookId) plan = await a.post('/api/woodpecker', { bookIds: [Number(p.bookId)], startedAt: null });
    return plan ? { woodpeckerPlanId: plan.id } : { _why: 'kein Aufgabenkurs für einen Plan' };
  });
  // Lern-Kurs (Kind „Study", kein Aufgabenbuch) und Kurz-URL eines öffentlichen Kurses (/{slug}). Art, „öffentlich" und
  // Alias stehen nur in der Detailantwort, nicht in der Liste. Hat kein öffentlicher Kurs einen Alias, macht das
  // Admin-Konto den ersten Aufgabenkurs öffentlich mit dem Alias „ui-sweep" (Dev-Daten; das normale Konto darf das nicht).
  await step(['studyBookId', 'courseSlug', 'courseSlugChapter'], async () => {
    const details = [];
    for (const c of list(await a.get('/api/courses')).filter(c => !c.isCalculation)) details.push(await a.get(`/api/courses/${c.bookId}`));
    const study = details.find(d => d.kind === 'Study');
    let slug = details.find(d => d.isPublic && d.publicSlug)?.publicSlug;
    const target = details.find(d => d.kind !== 'Study' && d.canManage && (d.puzzleCount ?? 0) > 20);
    if (!slug && target && only === null) {
      try {
        await a.put(`/api/admin/books/${target.bookId}`, { isPublic: true, publicSlug: SWEEP_TAG });
        slug = SWEEP_TAG;
      } catch { /* kein Admin */ }
    }
    // Kapitel-Kurzlink /{slug}/{kapitel}: der Kapitel-Teil IST der Kapitelname — das erste Kapitel mit Aufgaben.
    let courseSlugChapter;
    if (slug) {
      try {
        const { bookId } = await a.get(`/api/courses/by-slug/${encodeURIComponent(slug)}`);
        const chapters = list(await a.get(`/api/courses/${bookId}/chapters`));
        const ch = chapters.find(c => (c.puzzleCount ?? 0) > 0) ?? chapters[0];
        if (ch?.name) courseSlugChapter = encodeURIComponent(ch.name);
      } catch { /* Kurs nicht lesbar */ }
    }
    return { studyBookId: study?.bookId, courseSlug: slug, courseSlugChapter, _why: study ? 'kein öffentlicher Kurs mit Alias' : 'kein Lern-Kurs sichtbar' };
  });

  // Zufallspuzzle nur beim ersten Mal ziehen — sonst wäre die Seite bei jedem Vergleich „verändert".
  await step(['puzzleId'], async () => ({ puzzleId: previous.puzzleId ?? (await a.get('/api/puzzles/random')).id }));
  await step(['weeklyId'], async () => ({ weeklyId: list(await a.get('/api/weekly-posts'))[0]?.id }));
  // /guess/:id ist eine SPIELSITZUNG, keine Analyse: die eigene vorhandene nehmen, sonst eine zur ersten Partie des Bestands starten.
  await step(['guessId'], async () => {
    const own = list(await a.get('/api/guess-sessions'))[0];
    if (own) return { guessId: own.id };
    const game = list(await a.get('/api/game-analyses/public'))[0];
    return game ? { guessId: (await a.post('/api/guess-sessions', { gameAnalysisId: game.id })).id } : {};
  });
  // Eine FERTIGE eigene Analyse (die über „Partie analysieren" angestoßene zählt mit — siehe oben). Halbfertige zeigten bei
  // jedem Lauf einen anderen Stand.
  await step(['analysisId'], async () => {
    const all = list(await a.get('/api/game-analyses?includeSavedGames=true'));
    const done = all.find(x => x.status === 'done');
    return { analysisId: done?.id, _why: done ? undefined : all.length ? `${all.length} Analyse(n), keine fertig` : noEngine ?? 'keine eigene Analyse' };
  });
  // Zugvergleich: einen anlegen, wenn es keinen gibt und eine Engine rechnet; genommen wird ein fertiger.
  await step(['comparisonId'], async () => {
    let all = list(await a.get('/api/move-comparisons'));
    if (!all.length) {
      const status = await a.get('/api/move-comparisons/status');
      if (!status.engineAvailable) return { _why: noEngine ?? 'keine Engine für Zugvergleiche' };
      await a.post('/api/move-comparisons', { fen: COMPARE_FEN, moves: ['d2d4', 'c2c3', 'b1c3'], title: SWEEP_TAG, lang: 'de' });
      all = list(await a.get('/api/move-comparisons'));
    }
    const done = all.find(x => x.status === 'done');
    return { comparisonId: done?.id, _why: done ? undefined : `Vergleich läuft noch (${all[0]?.status})` };
  });
  await step(['prepId'], async () => ({ prepId: list(await a.get('/api/prep/players?q=Carlsen'))[0]?.id }));
  await step(['friendId'], async () => {
    const f = list(await a.get('/api/friends'))[0];
    return { friendId: f?.userId ?? f?.friendId ?? f?.id };
  });
  await step(['clubGameId'], async () => ({ clubGameId: list(await a.get('/api/league/club/games?club=1'))[0]?.id }));
  await step(['clubMemberId'], async () => ({ clubMemberId: list(await a.get('/api/club/members')).find(m => !m.isTrainer)?.id }));
  await step(['clubGroupId'], async () => ({ clubGroupId: list(await a.get('/api/club/groups'))[0]?.id }));
  await step(['kidsBookId'], async () => ({ kidsBookId: list(await a.get('/api/kids/courses'))[0]?.bookId }));
  await step(['tournamentId'], async () => ({ tournamentId: list(await a.get('/api/tournaments'))[0]?.id }));
  await step(['directoryId'], async () => {
    const from = new Date().toISOString().slice(0, 10);
    return { directoryId: list(await a.get(`/api/tournament-directory?from=${from}&pageSize=1`))[0]?.id };
  });

  // Teilen-Link einer Begegnung in LeagueHub: erste Liga des ersten Vereins, nächste offene Runde, eigene Mannschaft.
  await step(['leagueShareToken'], async () => {
    // Den Link vom letzten Lauf weiterbenutzen, solange er gilt — Teilen-Links lassen sich nicht auflisten.
    if (previous.leagueShareToken) {
      const ok = await fetch(`${base}/api/league/s/${previous.leagueShareToken}`).then(r => r.ok, () => false);
      if (ok) return { leagueShareToken: previous.leagueShareToken };
    }
    const me = await a.get('/api/league/me');
    const club = me.clubs?.[0];
    if (!club) return {};
    const index = await a.get(`/api/league/index?club=${club.id}`);
    for (const league of index.leagues ?? []) {
      const view = await a.get(`/api/league/${league.tnr}?club=${club.id}`);
      const round = (view.rounds ?? []).find(x => x.open);
      const team = (view.teams ?? []).find(t => t.toLowerCase().startsWith(club.teamPrefix.toLowerCase()));
      if (!round || !team) continue;
      const s = await a.post(`/api/league/share?club=${club.id}`, { tnr: league.tnr, round: round.round, team });
      return { leagueShareToken: s.token };
    }
    return {};
  });

  // Teilen-Link einer GESPIELTEN Begegnung samt einer Vereinspartie an einem ihrer Bretter — für RookHubs
  // `/s/{token}/club-games/{id}` (0.838.3). Der Link oben zeigt die nächste offene Runde, dort hängt noch keine Partie
  // (404 im Lauf vom 28.10.). Höchstens 8 Versuche, jüngste gespielte Runden zuerst.
  await step(['leagueShareGameToken', 'shareClubGameId'], async () => {
    const firstGame = o => {
      if (Array.isArray(o)) { for (const x of o) { const g = firstGame(x); if (g) return g; } return null; }
      if (o && typeof o === 'object') {
        if (Number.isInteger(o.clubGameId)) return o.clubGameId;
        for (const v of Object.values(o)) { const g = firstGame(v); if (g) return g; }
      }
      return null;
    };
    const tryToken = async token => {
      // Die Begegnung selbst trägt nur die Prognose; die Partien an den Brettern kommen aus `…/games` (lineups.ts).
      const r = await fetch(`${base}/api/league/s/${token}/games`).then(x => x.ok ? x.json() : null, () => null);
      return r && firstGame(r);
    };
    if (previous.leagueShareGameToken) {
      const g = await tryToken(previous.leagueShareGameToken);
      if (g) return { leagueShareGameToken: previous.leagueShareGameToken, shareClubGameId: g };
    }
    const me = await a.get('/api/league/me');
    const club = me.clubs?.[0];
    if (!club) return {};
    // Vereinspartien mit Ligapaarung tragen „2026/27 · 1. Klasse · Runde 2 · Brett 5 (…)“ — daraus Liga + Runde.
    const index = await a.get(`/api/league/index?club=${club.id}`);
    const games = list(await a.get(`/api/league/club/games?club=${club.id}`)).filter(g => g.leagueGameId && g.leagueGameLabel);
    let tries = 0;
    for (const g of games) {
      const m = /^[^·]+·\s*(.+?)\s*·\s*Runde\s+(\d+)\s*·/.exec(g.leagueGameLabel);
      const league = m && (index.leagues ?? []).find(l => l.name === m[1] && l.own?.length);
      if (!league) continue;
      if (++tries > 4) break;
      const s = await a.post(`/api/league/share?club=${club.id}`, { tnr: league.tnr, round: Number(m[2]), team: league.own[0] });
      const id = await tryToken(s.token);
      if (id) return { leagueShareGameToken: s.token, shareClubGameId: id };
    }
    return { _why: 'keine Vereinspartie an einer Ligapaarung des eigenen Teams' };
  });

  // Offene Formular-Einlesung in LeagueHub (/verein/formular/{id}). Dev liest Formulare nicht selbst
  // (`Scoresheet:Reader=external`, der Watcher schaut nur auf Prod) — ein hochgeladenes Bild bleibt also „wartet aufs
  // Einlesen" stehen, ohne den Anthropic-Schlüssel anzufassen. Hochgeladen wird nur, wenn das Konto keine offene hat.
  await step(['scanId'], async () => {
    const club = (await a.get('/api/league/me')).clubs?.[0];
    if (!club) return { _why: 'kein Verein (league.contribute fehlt)' };
    const open = list(await a.get(`/api/league/club/scans?club=${club.id}`));
    if (open.length) return { scanId: open[0].id };
    const scan = await a.post(`/api/league/club/scans?club=${club.id}`, scanForm());
    return { scanId: scan?.id };
  });
  // Dasselbe ohne Konto über den Teilen-Link (/s/{token}/formular/{key}) — der Schlüssel lässt sich nicht auflisten,
  // also wie der Teilen-Link aus dem letzten Lauf weiterbenutzen, solange die Einlesung offen ist.
  await step(['shareScanKey'], async () => {
    const token = p.leagueShareToken;
    if (!token) return { _why: 'kein LeagueHub-Teilen-Link' };
    const anon = api(base, null);
    if (previous.shareScanKey) {
      const found = await anon.post(`/api/league/s/${token}/club/scans/lookup`, { keys: [previous.shareScanKey] }).catch(() => []);
      if (list(found).length) return { shareScanKey: previous.shareScanKey };
    }
    const r = await anon.post(`/api/league/s/${token}/club/scans`, scanForm());
    return { shareScanKey: r?.key };
  });

  return p;
}

/** E-Book-Hilfen für die Resolver: Freigabe „Everyone" setzen und je Rolle nachprüfen, Datei ladbar? */
function ebookHelpers(base, a, viewers) {
  let everyone;
  const everyoneId = async () => everyone ??= (await a.get('/api/ebooks/groups')).find(g => g.isEveryone)?.id;
  // Sieht jede Rolle (auch abgemeldet) das Buch? Ohne Drossel-Pause genügt hier ein schlichtes fetch.
  const visibleToAll = async id => {
    for (const tok of [null, ...Object.values(viewers)]) {
      const res = await fetch(`${base}/api/ebooks/${id}`, { headers: tok ? { Authorization: `Bearer ${tok}` } : {} });
      await res.arrayBuffer().catch(() => null);
      if (!res.ok) return false;
    }
    return true;
  };
  return {
    everyoneId,
    visibleToAll,
    /** Lädt die Buchdatei (erste Bytes)? Dev hat nicht jede Datei der Bibliothek. */
    fileLoads: async id => {
      const { token } = await a.get('/api/ebooks/access-token');
      const res = await fetch(`${base}/api/ebooks/${id}/file?t=${encodeURIComponent(token)}`, { headers: { Range: 'bytes=0-15' } });
      await res.arrayBuffer().catch(() => null);
      return res.ok;
    },
    /** Für alle sichtbar machen (Gruppe „Everyone" ZUSÄTZLICH zu den bisherigen) — nur das Admin-Konto (`only` null). */
    shareWithAll: async (book, only) => {
      if (await visibleToAll(book.id)) return true;
      const id = await everyoneId();
      if (only !== null || id == null) return false;
      await a.put('/api/ebooks/groups', { ebookIds: [book.id], groupIds: [...new Set([...(book.groupIds ?? []), id])] });
      return visibleToAll(book.id);
    },
  };
}

/** Engine-unabhängige Parameter eines Kontos mit eingeschränkter Sicht (Rolle „trainer"): nur was ER in ClubHub sieht —
 *  ein Kind und eine Gruppe, denen er zugeteilt ist. Legt nichts an. */
export async function resolveClubParams(base, token, log = () => {}) {
  const a = api(base, token);
  const p = {};
  for (const [name, fn] of [
    ['clubGroupId', async () => list(await a.get('/api/club/groups'))[0]?.id],
    ['clubMemberId', async () => list(await a.get('/api/club/members')).find(m => !m.isTrainer)?.id],
  ]) {
    try { const v = await fn(); if (v != null) p[name] = String(v); log(name, v ?? null); } catch (e) { log(name, e); }
  }
  return p;
}

/** ClubHub-Trainer einrichten (Rolle „trainer"): Rolle mit `club.trainer` über eine eigene Gruppe, dazu als Konto mit
 *  Zugriff auf die ERSTE Kindergruppe zugeteilt. Wiederholbar; braucht das Admin-Konto. Gibt die Gruppe zurück. */
export async function ensureTrainer(base, adminToken, trainer) {
  const A = api(base, adminToken);
  let role = (await A.get('/api/admin/roles')).find(r => r.key === TRAINER_ROLE.key);
  if (!role) role = await A.post('/api/admin/roles', TRAINER_ROLE);
  let group = (await A.get('/api/admin/groups')).find(g => g.name === TRAINER_ROLE.name);
  if (!group) group = await A.post('/api/admin/groups', { name: TRAINER_ROLE.name, description: 'Trainer-Konto des UI-Sweeps' });
  const cur = await A.get(`/api/admin/groups/${group.id}/roles`);
  const ids = cur.roleIds ?? cur.map?.(r => r.id) ?? [];
  if (!ids.includes(role.id)) await A.put(`/api/admin/groups/${group.id}/roles`, { roleIds: [...ids, role.id] });
  await A.post(`/api/admin/groups/${group.id}/members/${trainer.userId}`);
  const clubGroup = list(await A.get('/api/club/groups')).find(g => !g.archived);
  if (!clubGroup) throw new Error('keine ClubHub-Gruppe auf Dev');
  if (!(clubGroup.trainers ?? []).some(t => t.userId === trainer.userId))
    await A.post(`/api/club/groups/${clubGroup.id}/trainers`, { username: trainer.username });
  return clubGroup;
}

const TRAINER_ROLE = { key: 'clubhub-trainer-ui-sweep', name: 'ClubHub-Trainer (ui-sweep)', permissions: ['club.trainer'] };

/** Ein Formular-„Foto" für den Upload: weißes Blatt mit Linien wie ein Partieformular (PNG, mit pngjs erzeugt). */
function scanForm() {
  const w = 600, h = 840;
  const png = new PNG({ width: w, height: h });
  for (let y = 0; y < h; y++) for (let x = 0; x < w; x++) {
    const i = (y * w + x) * 4;
    const line = (y > 80 && y < 800 && (y - 80) % 24 === 0 && x > 30 && x < 570) || ((x === 30 || x === 300 || x === 570) && y > 80 && y < 800)
      || (y === 40 && x > 30 && x < 570);
    const v = line ? 40 : 250;
    png.data[i] = png.data[i + 1] = png.data[i + 2] = v; png.data[i + 3] = 255;
  }
  const fd = new FormData();
  fd.append('file', new Blob([PNG.sync.write(png)], { type: 'image/png' }), `${SWEEP_TAG}-formular.png`);
  fd.append('language', 'de');
  fd.append('side', 'auto');
  return fd;
}

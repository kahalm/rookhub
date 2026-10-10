// Parameter für die Routen mit Platzhaltern ({gameId}, {bookId} …) — über die API gesucht, und wo das Konto
// noch nichts hat, angelegt. Angelegtes trägt „ui-sweep" im Namen und wird beim nächsten Lauf wiedergefunden,
// es wächst also nichts nach (Ausnahme: der LeagueHub-Teilen-Link, Teilen-Links lassen sich nicht auflisten).
//
// Jeder Resolver ist für sich: scheitert einer, fehlt nur sein Parameter, und die betroffenen Routen werden
// übersprungen (im Bericht unter „nicht auflösbar").

const SWEEP_TAG = 'ui-sweep';

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
  const res = await fetch(`${base}/api/auth/login`, {
    method: 'POST', headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ username: user, password }),
  });
  if (!res.ok) throw new Error(`Anmeldung an ${base} gescheitert: HTTP ${res.status}`);
  return res.json();   // AuthResponse — genau das legt die App als `rookhub_user` in den localStorage
}

function api(base, token) {
  const call = async (method, path, body) => {
    await new Promise(r => setTimeout(r, 400));   // Drossel der API (100/min je Adresse) — nach einem 429 fehlten ganze Parameter
    const res = await fetch(`${base}${path}`, {
      method,
      headers: { Authorization: `Bearer ${token}`, ...(body !== undefined ? { 'content-type': 'application/json' } : {}) },
      body: body !== undefined ? JSON.stringify(body) : undefined,
    });
    if (!res.ok) throw new Error(`${method} ${path} → HTTP ${res.status}`);
    const text = await res.text();
    return text ? JSON.parse(text) : null;
  };
  return { get: p => call('GET', p), post: (p, b) => call('POST', p, b ?? {}) };
}

const list = x => Array.isArray(x) ? x : (x?.items ?? []);

/** Sucht/legt alle Parameter an. `log(name, wert | Fehler)` meldet jeden Resolver. */
export async function resolveParams(base, token, log = () => {}, previous = {}) {
  const a = api(base, token);
  const p = {};
  const today = new Date();
  // UTC-Datum: der Server kennt das Tagespuzzle nur für heute/gestern in UTC — kurz nach Mitternacht in Wien wäre das Ortsdatum schon morgen (400).
  p.today = `${today.getUTCFullYear()}${String(today.getUTCMonth() + 1).padStart(2, '0')}${String(today.getUTCDate()).padStart(2, '0')}`;

  const step = async (names, fn) => {
    try {
      const out = await fn();
      for (const n of names) {
        if (out?.[n] !== undefined && out[n] !== null && out[n] !== '') { p[n] = String(out[n]); log(n, p[n]); }
        else log(n, null);
      }
    } catch (e) { for (const n of names) log(n, e); }
  };

  // Eigene Partie (angelegt per PGN-Import) + Teilen-Link; eine schon analysierte eigene Partie, wenn es eine gibt.
  await step(['gameId', 'gameShareToken', 'analyzedGameId', 'analyzedShareToken'], async () => {
    let games = list(await a.get('/api/games?take=200'));
    if (!games.some(g => g.white === 'Sweep, Weiss')) {
      await a.post('/api/games/import', { pgn: SAMPLE_PGN });
      games = list(await a.get('/api/games?take=200'));
    }
    const seed = games.find(g => g.white === 'Sweep, Weiss');
    const analyzed = games.find(g => g.analysis?.status === 'done');
    if (seed && !analyzed && !seed.analysis) {
      // Einmal anstoßen — beim NÄCHSTEN Lauf gibt es dann eine analysierte Partie (Rechnen dauert Minuten).
      try { await a.post(`/api/games/${seed.id}/analyze`, {}); } catch { /* keine Engine auf Dev: dann eben nicht */ }
    }
    const share = async g => g ? (await a.get(`/api/games/${g.id}`)).shareToken : undefined;
    return {
      gameId: seed?.id, gameShareToken: await share(seed),
      analyzedGameId: analyzed?.id, analyzedShareToken: await share(analyzed),
    };
  });

  await step(['repertoireId'], async () => ({ repertoireId: list(await a.get('/api/repertoires'))[0]?.id }));

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

  await step(['bookId', 'calcBookId', 'bookPuzzleId'], async () => {
    const courses = list(await a.get('/api/courses'));
    const book = courses.find(c => !c.isCalculation && (c.puzzleCount ?? 0) > 20) ?? courses.find(c => !c.isCalculation);
    const calc = courses.find(c => c.isCalculation);
    let bookPuzzleId;
    if (book) {
      const next = await a.get(`/api/courses/${book.bookId}/next?mode=sequential`);
      bookPuzzleId = next?.puzzle?.id ?? next?.id;
    }
    return { bookId: book?.bookId, calcBookId: calc?.bookId, bookPuzzleId };
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
  await step(['analysisId'], async () => ({ analysisId: list(await a.get('/api/game-analyses'))[0]?.id }));
  await step(['comparisonId'], async () => ({ comparisonId: list(await a.get('/api/move-comparisons'))[0]?.id }));
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

  return p;
}

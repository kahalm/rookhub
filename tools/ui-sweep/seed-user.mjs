// Gibt dem normalen Testkonto (Rolle „user", ROOKHUB_DEV_PLAIN_USER) einen Bestand wie ein Nutzer auf Prod — nur Dev.
// Wiederholbar: was schon da ist, wird erkannt und nicht doppelt angelegt.
//
//   node seed-user.mjs            (Dev-API wie sweep.mjs; Zugang aus ~/.config/rookhub/dev-claude.env)
//
// Was entsteht:
//   - Kurse über eine Gruppe „Testverein (ui-sweep)" freigegeben, drei angepinnt, drei angefangen
//   - vier Repertoires (Eröffnung Weiß/Schwarz, Endspiel), ein paar eigene Partien, ein Aufgabenblatt
//   - Trainingsziel, Puzzle-Versuche, ein Endlos-Lauf, Freundschaft mit dem Admin-Konto
//   - LeagueHub: Mitglied der Gruppe Schwaz (Rolle „Verein Schwaz" = league.view + league.contribute, wie Prod)
//   - KidHub: geschaffte Stufen und gelöste Kinderkurse; ClubHub: mit einem Karteiblatt verknüpft (Lernstand)

import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';

const BASE = process.env.UI_SWEEP_API ?? 'http://127.0.0.1:5002';
const env = Object.fromEntries(fs.readFileSync(path.join(os.homedir(), '.config/rookhub/dev-claude.env'), 'utf8')
  .split('\n').filter(l => l.includes('=')).map(l => [l.slice(0, l.indexOf('=')).trim(), l.slice(l.indexOf('=') + 1).trim()]));

async function login(user, password) {
  const r = await fetch(`${BASE}/api/auth/login`, { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ username: user, password }) });
  if (!r.ok) throw new Error(`Anmeldung ${user}: HTTP ${r.status}`);
  return r.json();
}
function api(token) {
  const call = async (method, p, body, raw) => {
    await new Promise(r => setTimeout(r, 700));   // Drossel der API: 100 Anfragen/min je Adresse
    const headers = { Authorization: `Bearer ${token}` };
    if (body !== undefined && !(body instanceof FormData)) headers['content-type'] = 'application/json';
    const r = await fetch(`${BASE}${p}`, { method, headers, body: body === undefined ? undefined : body instanceof FormData ? body : JSON.stringify(body) });
    if (!r.ok) throw new Error(`${method} ${p} → HTTP ${r.status} ${(await r.text()).slice(0, 200)}`);
    const t = await r.text();
    return raw ? t : t ? JSON.parse(t) : null;
  };
  return { get: p => call('GET', p), text: p => call('GET', p, undefined, true), post: (p, b) => call('POST', p, b ?? {}),
    put: (p, b) => call('PUT', p, b ?? {}), form: (p, fd) => call('POST', p, fd) };
}
const step = async (name, fn) => {
  try { console.log(`✓ ${name}: ${(await fn()) ?? 'ok'}`); } catch (e) { console.log(`✗ ${name}: ${e.message}`); }
};

const adminS = await login(env.ROOKHUB_DEV_USER, env.ROOKHUB_DEV_PASSWORD);
const userS = await login(env.ROOKHUB_DEV_PLAIN_USER, env.ROOKHUB_DEV_PLAIN_PASSWORD);
const A = api(adminS.token), U = api(userS.token);
const uid = userS.userId;

// Kurse, die das Konto über seine Gruppe sieht (Dev-Bücher), davon angepinnt / angefangen.
const COURSES = [45, 57, 41, 52, 49];
const PINNED = [45, 57, 52];
const STARTED = { 45: [12, 10], 57: [8, 6], 41: [5, 5] };   // [Versuche, davon gelöst]

await step('Gruppe „Testverein (ui-sweep)" + Kursfreigaben', async () => {
  const groups = await A.get('/api/admin/groups');
  let g = groups.find(x => x.name === 'Testverein (ui-sweep)');
  if (!g) g = await A.post('/api/admin/groups', { name: 'Testverein (ui-sweep)', description: 'Testkonto des UI-Sweeps' });
  await A.post(`/api/admin/groups/${g.id}/members/${uid}`);
  for (const b of COURSES) {
    const cur = await A.get(`/api/admin/books/${b}/groups`);
    const ids = Array.isArray(cur) ? cur : (cur.groupIds ?? []);
    if (!ids.includes(g.id)) await A.put(`/api/admin/books/${b}/groups`, { groupIds: [...ids, g.id] });
  }
  return `Gruppe ${g.id}, ${COURSES.length} Kurse`;
});

await step('Kurse anpinnen', async () => { for (const b of PINNED) await U.post(`/api/courses/${b}/pin`); return PINNED.join(', '); });

await step('Kurse anfangen', async () => {
  const list = await U.get('/api/courses');
  const out = [];
  for (const [b, [n, solved]] of Object.entries(STARTED)) {
    const c = list.find(x => x.bookId === +b);
    if (c && c.solvedCount > 0) { out.push(`${b} schon`); continue; }
    const puzzles = (await U.get(`/api/courses/${b}/puzzles`)).filter(p => !p.isInfoOnly).slice(0, n);
    for (let i = 0; i < puzzles.length; i++)
      await U.post(`/api/courses/${b}/results`, { bookPuzzleId: puzzles[i].id, solved: i < solved, mode: 'sequential', timeSeconds: 40 + i * 7 });
    out.push(`${b}: ${puzzles.length}`);
  }
  return out.join(', ');
});

const CARO = `[Event "Caro-Kann"]
[White "?"]
[Black "?"]
[Result "*"]

1. e4 c6 2. d4 d5 3. Nc3 dxe4 4. Nxe4 Bf5 5. Ng3 Bg6 6. h4 h6 7. Nf3 Nd7 8. h5 Bh7 9. Bd3 Bxd3 10. Qxd3 e6 *

[Event "Caro-Kann"]
[White "?"]
[Black "?"]
[Result "*"]

1. e4 c6 2. d4 d5 3. e5 Bf5 4. Nf3 e6 5. Be2 c5 6. Be3 Nd7 7. O-O Ne7 *

[Event "Caro-Kann"]
[White "?"]
[Black "?"]
[Result "*"]

1. e4 c6 2. d4 d5 3. exd5 cxd5 4. Bd3 Nc6 5. c3 Nf6 6. Bf4 Bg4 7. Qb3 Qd7 *
`;
const ITALIAN = `[Event "Italienisch"]
[White "?"]
[Black "?"]
[Result "*"]

1. e4 e5 2. Nf3 Nc6 3. Bc4 Bc5 4. c3 Nf6 5. d3 d6 6. O-O O-O 7. Re1 a6 8. Bb3 Ba7 9. h3 *

[Event "Italienisch"]
[White "?"]
[Black "?"]
[Result "*"]

1. e4 e5 2. Nf3 Nc6 3. Bc4 Nf6 4. d3 Be7 5. O-O O-O 6. Re1 d6 7. c3 Na5 8. Bc2 c5 *

[Event "Italienisch"]
[White "?"]
[Black "?"]
[Result "*"]

1. e4 c5 2. c3 Nf6 3. e5 Nd5 4. d4 cxd4 5. Nf3 Nc6 6. cxd4 d6 7. Bc4 Nb6 8. Bb5 *
`;

await step('Repertoires', async () => {
  const have = (await U.get('/api/repertoires')).map(r => r.name);
  const reps = [
    { name: 'Weiß: Italienisch + Alapin', kind: 'Opening', pgn: async () => ITALIAN },
    { name: 'Schwarz gegen 1.e4: Caro-Kann', kind: 'Opening', pgn: async () => CARO },
    { name: 'Schwarz gegen 1.d4: Grünfeld', kind: 'Opening', pgn: () => A.text('/api/courses/52/pgn') },
    { name: 'Grundendspiele', kind: 'Endgame', pgn: () => A.text('/api/courses/41/pgn') },
  ];
  const made = [];
  for (const r of reps) {
    if (have.includes(r.name)) continue;
    const rep = await U.post('/api/repertoires', { name: r.name, description: '', isPublic: false, kind: r.kind });
    const fd = new FormData();
    fd.append('file', new Blob([await r.pgn()], { type: 'application/x-chess-pgn' }), `${r.name.replace(/[^\w]+/g, '_')}.pgn`);
    await U.form(`/api/repertoires/${rep.id}/files`, fd);
    made.push(r.name);
  }
  return made.length ? made.join(' · ') : 'schon da';
});

const GAMES = [
  ['Testkonto, Lena', 'Huber, Max', '1-0', '1. e4 e5 2. Nf3 Nc6 3. Bc4 Bc5 4. c3 Nf6 5. d4 exd4 6. cxd4 Bb4+ 7. Nc3 Nxe4 8. O-O Bxc3 9. d5 Bf6 10. Re1 Ne7 11. Rxe4 d6 12. Bg5 Bxg5 13. Nxg5 h6 14. Qe2 hxg5 15. Re1 Be6 16. dxe6 f6 17. Re3 c6 18. Rh3 Rxh3 19. gxh3 g6 20. Qf3 1-0'],
  ['Moser, Anna', 'Testkonto, Lena', '0-1', '1. d4 d5 2. c4 e6 3. Nc3 Nf6 4. Bg5 Be7 5. e3 O-O 6. Nf3 Nbd7 7. Rc1 c6 8. Bd3 dxc4 9. Bxc4 Nd5 10. Bxe7 Qxe7 11. O-O Nxc3 12. Rxc3 e5 13. Qc2 exd4 14. exd4 Nf6 15. Re1 Qd6 16. Ne5 Be6 17. Bxe6 fxe6 18. Rf3 Nd5 19. Rh3 Rf4 20. Nf3 Raf8 0-1'],
  ['Testkonto, Lena', 'Gruber, Felix', '1/2-1/2', '1. e4 c5 2. c3 Nf6 3. e5 Nd5 4. d4 cxd4 5. Nf3 Nc6 6. cxd4 d6 7. Bc4 Nb6 8. Bb5 dxe5 9. Nxe5 Bd7 10. Nxd7 Qxd7 11. Nc3 e6 12. O-O Be7 13. Qg4 O-O 14. Bh6 Bf6 15. Rad1 Kh8 16. Bf4 Rad8 1/2-1/2'],
];
await step('Eigene Partien', async () => {
  const pgn = GAMES.map(([w, b, r, m], i) => `[Event "Vereinsabend"]\n[Site "Dev"]\n[Date "2026.09.${String(10 + i).padStart(2, '0')}"]\n[White "${w}"]\n[Black "${b}"]\n[Result "${r}"]\n\n${m}\n`).join('\n');
  const res = await U.post('/api/games/import', { pgn });
  return `${res.imported} neu, ${res.duplicates} schon da` + (res.failed?.length ? `, abgelehnt: ${JSON.stringify(res.failed)}` : '');
});

await step('Aufgabenblatt', async () => {
  const sheets = await U.get('/api/worksheets');
  if (sheets.some(s => s.name === 'Mittwochstraining U12')) return 'schon da';
  await U.post('/api/worksheets/items', { items: [
    { fen: 'r1bqkb1r/pppp1ppp/2n2n2/4p2Q/2B1P3/8/PPPP1PPP/RNB1K1NR w KQkq - 4 4', orientation: 'white', heading: 'Matt in 1' },
    { fen: '6k1/5ppp/8/8/8/8/5PPP/3R2K1 w - - 0 1', orientation: 'white', heading: 'Grundreihe' },
    { fen: 'r3k2r/ppp2ppp/2n5/3q4/3P4/2N5/PPP2PPP/R2QK2R w KQkq - 0 1', orientation: 'white', heading: 'Gabel finden' },
  ] });
  await U.post('/api/worksheets/clipboard/save', { name: 'Mittwochstraining U12', perPage: 4 });
  return 'angelegt';
});

await step('Trainingsziel', () => U.put('/api/training-goals', { dailyMinutes: 20, playGames: 2, weeklyDaysTarget: 4 }).then(() => '20 min, 4 Tage'));

await step('Puzzle-Versuche', async () => {
  const stats = await U.get('/api/puzzles/stats');
  if ((stats.totalAttempts ?? 0) >= 10) return `schon ${stats.totalAttempts}`;
  let n = 0;
  for (let i = 0; i < 14; i++) {
    const p = await U.get('/api/puzzles/random');
    await U.post(`/api/puzzles/${p.id}/attempt`, { solved: i % 4 !== 3, timeSpentSeconds: 25 + i * 3 });
    n++;
  }
  return `${n} Versuche`;
});

await step('Endlos-Lauf', async () => {
  const prog = await U.get('/api/endless/progress');
  if ((prog.sessions ?? []).length) return 'schon da';
  await U.post('/api/endless/sessions', { timestamp: Date.now() - 86400000, totalSolved: 23, maxRating: 1480, durationSeconds: 1260, configJson: '{}', puzzles: [] });
  return '23 gelöst';
});

await step('Freundschaft mit dem Admin-Konto', async () => {
  const friends = await U.get('/api/friends');
  if (friends.some(f => (f.userId ?? f.friendId ?? f.id) === adminS.userId || f.username === adminS.username)) return 'schon befreundet';
  try { await U.post(`/api/friends/request/${adminS.userId}`); } catch { /* schon angefragt */ }
  const req = (await A.get('/api/friends/requests')).find(r => (r.requesterId ?? r.fromUserId ?? r.userId) === uid || r.username === userS.username);
  if (req) await A.post(`/api/friends/accept/${req.friendshipId ?? req.id}`);
  return 'befreundet';
});

await step('LeagueHub: Gruppe Schwaz mit Rolle „Verein Schwaz"', async () => {
  const roles = await A.get('/api/admin/roles');
  let role = roles.find(r => r.key === 'verein-schwaz' || r.name === 'Verein Schwaz');
  if (!role) role = await A.post('/api/admin/roles', { key: 'verein-schwaz', name: 'Verein Schwaz', permissions: ['league.view', 'league.contribute'] });
  const groups = await A.get('/api/admin/groups');
  const schwaz = groups.find(g => g.name === 'Schwaz');
  if (!schwaz) throw new Error('Gruppe Schwaz fehlt auf Dev');
  const cur = await A.get(`/api/admin/groups/${schwaz.id}/roles`);
  const ids = cur.roleIds ?? cur.map?.(r => r.id) ?? [];
  if (!ids.includes(role.id)) await A.put(`/api/admin/groups/${schwaz.id}/roles`, { roleIds: [...ids, role.id] });
  await A.post(`/api/league/admin/clubs/1/groups/${schwaz.id}`);
  await A.post(`/api/admin/groups/${schwaz.id}/members/${uid}`);
  return `Gruppe ${schwaz.id} → Verein 1, Rolle ${role.id}`;
});

// KidHub: zwei Bücher als Kinderkurse freigeben (Dev hat keine), dann Fortschritt.
// Nur Bücher mit deutscher Quelle: KidHub zeigt einen Kinderkurs erst, wenn es ihn auf Deutsch gibt (Kids:RequiredCourseLanguages).
const KIDS_BOOKS = { 50: { de: 'Erste Endspiele', en: 'First endgames' } };
const NOT_KIDS = [41, 57];   // frühere Wahl (Quelle nicht deutsch → auf KidHub unsichtbar) wieder zurücknehmen
await step('KidHub: Kinderkurse freigeben', async () => {
  for (const [b, titles] of Object.entries(KIDS_BOOKS)) await A.put(`/api/admin/books/${b}`, { forKids: true, kidsTitles: titles });
  for (const b of NOT_KIDS) await A.put(`/api/admin/books/${b}`, { forKids: false });
  return Object.keys(KIDS_BOOKS).join(', ');
});
await step('KidHub: Stufen + Kurse', async () => {
  const now = Date.now();
  const levels = [3, 3, 2, 3, 2, 1].map((stars, i) => ({ level: i + 1, stars, runIndex: 0, runMistakes: 0, runAt: now - (7 - i) * 3600000 }));
  levels.push({ level: 7, stars: 0, runIndex: 4, runMistakes: 1, runAt: now - 600000 });
  const courses = [];
  for (const b of Object.keys(KIDS_BOOKS)) {
    const puzzles = await U.get(`/api/kids/courses/${b}/puzzles?lang=de`);
    courses.push({ bookId: +b, resetAt: 0, solved: puzzles.slice(0, 8).map((p, i) => ({ id: p.id, at: now - i * 60000 })) });
  }
  const res = await U.put('/api/kids/progress', { levels, courses });
  return `${res.levels.length} Stufen, ${res.courses.map(c => c.solved.length).join('+')} Kurslinien`;
});

await step('ClubHub: Karteiblatt verknüpfen', async () => {
  const state = await U.get('/api/club/link');
  if (state?.linked || state?.member) return 'schon verknüpft';
  const members = await A.get('/api/club/members');
  const list = Array.isArray(members) ? members : members.items ?? [];
  let m = list.find(x => x.firstName === 'Lena' && x.lastName === 'Testkonto');
  if (!m) m = await A.post('/api/club/members', { firstName: 'Lena', lastName: 'Testkonto', birthYear: 2015, level: 'Bauerndiplom',
    groupIds: [], contacts: [{ kind: 'phone', value: '+43 660 1234567', label: 'Mutter Daniela' }] });
  const code = await A.post(`/api/club/members/${m.id}/link-code`);
  await U.post('/api/club/link', { code: code.code });
  return `Blatt ${m.id}`;
});

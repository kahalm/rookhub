import { Fixture, LeagueRound, Phase } from './league.models';

// Reine Hilfsfunktionen (ohne Angular) — aus der Python/JS-Fassung übernommen.

export const pct = (p: number): string => `${Math.round(p * 100)} %`;

/** chess-results kürzt lange Teamnamen und lässt dabei „/" oder „-" am Ende stehen. */
export const tn = (s: string | null | undefined): string => String(s ?? '').replace(/[\s/-]+$/, '');

/** Für Nachrichten: „Spg Fügen-Mayrhofen/Zillertal/" → „Fügen-Mayrhofen/Zillertal". */
export const shortTeam = (s: string | null | undefined): string => tn(s).replace(/^Spg\.?\s+/i, '');

const DE: Record<string, string> = { N: 'S', B: 'L', R: 'T', Q: 'D', K: 'K' };

/** SAN englisch → deutsch (S, L, T, D): „1.e4 c5 2.Nf3" → „1.e4 c5 2.Sf3". */
export function de(s: string | null | undefined): string {
  return String(s ?? '')
    .replace(/(^|[\s.])([NBRQK])(?=[a-h1-8x])/g, (_m, a: string, p: string) => a + DE[p])
    .replace(/=([NBRQ])/g, (_m, p: string) => '=' + DE[p]);
}

export const NAME_WHITE: Record<string, string> = {
  e4: '1.e4', d4: '1.d4', c4: 'Englisch (1.c4)', Nf3: 'Réti (1.Sf3)', f4: 'Bird (1.f4)', g3: '1.g3', b3: 'Larsen (1.b3)',
};
export const NAME_VS_E4: Record<string, string> = {
  c5: 'Sizilianisch', e5: '1…e5', e6: 'Französisch', c6: 'Caro-Kann', d6: 'Pirc/Philidor (1…d6)', g6: 'Modern (1…g6)',
  d5: 'Skandinavisch', Nf6: 'Aljechin', b6: 'Owen', Nc6: 'Nimzowitsch',
};
export const NAME_VS_D4: Record<string, string> = {
  Nf6: 'Indisch (1…Sf6)', d5: '1…d5', f5: 'Holländisch', e6: '1…e6', g6: 'Modern (1…g6)', d6: '1…d6',
  c5: 'Benoni (1…c5)', b6: '1…b6', Nc6: 'Tschigorin (1…Sc6)',
};

export const PHASE_TEXT: Record<Phase, string> = {
  'R1': 'erste Runde der Saison, noch ohne Aufstellungen aus dieser Saison',
  'R2+': 'mit den bisherigen Runden dieser Saison',
  'So vorab': 'Sonntag vorab, der Samstag ist noch nicht gespielt',
  'So nach Sa': 'Sonntag, mit der Aufstellung vom Samstag',
};

export const SPEED = /blitz|rapid|schnell|bullet|armageddon/i;

export function roundLabel(r: LeagueRound): string {
  const tag = r.played ? 'gespielt' : r.open ? 'Prognose' : 'gesperrt';
  const d = r.date ? `, ${r.date.replace(/\d{4}$/, '')}` : '';
  return `Runde ${r.round}${d} (${tag})`;
}

/** „…, 6323 Bad Häring" → „Bad Häring"; sonst der erste Teil vor Komma/Klammer. */
export function venueShort(v: string | null | undefined): string {
  if (!v) return '';
  const m = v.match(/\b\d{4}\s+([^,;()]+?)\s*$/);
  return (m ? m[1] : v.split(/[,(;]/)[0]).trim();
}

/** „2024.05.01" → „01.05.2024" (unbekannte Teile „??" fallen weg). */
export function pgnDate(d: string | null | undefined): string {
  return String(d ?? '').replace(/\.\?\?/g, '').split('.').reverse().join('.');
}

/**
 * WhatsApp-Text einer Begegnung: pro Brett die drei Wahrscheinlichsten (Wunsch des Nutzers — „für die
 * Vorbereitung relevant"; derselbe Spieler darf an mehreren Brettern stehen), „andere x %" ab 15 %.
 */
export function shareText(leagueName: string, team: string, rnd: number, e: Fixture): string {
  const last = (n: string) => n.split(',')[0].trim();
  const count: Record<string, number> = {};
  (e.roster ?? []).forEach(r => { count[last(r.n)] = (count[last(r.n)] || 0) + 1; });
  const short = (n: string) => {
    const l = last(n);
    const first = (n.split(',')[1] || '').trim();
    return count[l] > 1 && first ? `${l} ${first[0]}.` : l;
  };
  const d = (e.date || '').replace(/\d{4}$/, '');
  const head = [`${leagueName} R${rnd}`, [d, e.time].filter(Boolean).join(', '), venueShort(e.venue)].filter(Boolean).join(' · ');
  const home = e.home ? team : e.opp!, away = e.home ? e.opp! : team;
  const fmt = (c: { n: string; elo: number | null; p: number }) => `${short(c.n)}${c.elo ? ` ${c.elo}` : ''} – ${Math.round(c.p * 100)} %`;
  const lines = (e.boards ?? []).map(b => {
    const sq = b.opp_color === 'w' ? '⬜' : '⬛';
    const cs = b.cand.filter(c => c.p >= 0.02);
    if (!cs.length) return `${b.board} ${sq} offen`;
    const rest = cs.slice(1).map(fmt);
    if (b.other >= 0.15) rest.push(`andere ${Math.round(b.other * 100)} %`);
    return `${b.board} ${sq} *${fmt(cs[0])}*${rest.length ? `\n     ${rest.join(', ')}` : ''}`;
  });
  const foot = ({
    'R1': 'Prognose aus früheren Saisonen, Runde 1 ist die unsicherste.',
    'R2+': 'Prognose aus früheren Saisonen und den bisherigen Runden.',
    'So vorab': 'Prognose vor dem Samstag, nach dem Samstag wird sie genauer.',
    'So nach Sa': 'Prognose mit der Aufstellung vom Samstag.',
  } as Record<string, string>)[e.phase ?? ''] ?? 'Prognose aus früheren Saisonen.';
  return [`*${head}*`, `*${shortTeam(home)} – ${shortTeam(away)}*`,
    'Tipp Gegner je Brett (% = sitzt an diesem Brett, ⬜/⬛ = Farbe des Gegners)', '', ...lines, '', foot].join('\n');
}

import { anonKeys, draftKeys, rememberAnonKey, rememberDraftKey } from './club-anon-keys';

describe('club-anon-keys', () => {
  beforeEach(() => { localStorage.removeItem('lh-anon-scans'); localStorage.removeItem('lh-anon-drafts'); });
  afterEach(() => { localStorage.removeItem('lh-anon-scans'); localStorage.removeItem('lh-anon-drafts'); });

  it('merkt Schlüssel je Teilen-Link, der jüngste vorn, ohne Doppel, höchstens 20', () => {
    rememberAnonKey('TOK', 'a');
    rememberAnonKey('TOK', 'b');
    rememberAnonKey('TOK', 'a');
    rememberAnonKey('OTHER', 'x');
    expect(anonKeys('TOK')).toEqual(['a', 'b']);
    expect(anonKeys('OTHER')).toEqual(['x']);
    for (let i = 0; i < 25; i++) rememberAnonKey('TOK', `k${i}`);
    expect(anonKeys('TOK').length).toBe(20);
    expect(anonKeys('TOK')[0]).toBe('k24');
  });

  it('vergisst einen Schlüssel mit keep=false', () => {
    rememberAnonKey('TOK', 'a');
    rememberAnonKey('TOK', 'b');
    rememberAnonKey('TOK', 'a', false);
    expect(anonKeys('TOK')).toEqual(['b']);
  });

  it('Entwurfs-Schlüssel liegen getrennt von den Formular-Schlüsseln', () => {
    rememberDraftKey('TOK', 'd1');
    expect(draftKeys('TOK')).toEqual(['d1']);
    expect(anonKeys('TOK')).toEqual([]);
    expect(JSON.parse(localStorage.getItem('lh-anon-drafts')!)).toEqual({ TOK: ['d1'] });
    rememberDraftKey('TOK', 'd1', false);
    expect(draftKeys('TOK')).toEqual([]);
  });

  it('ohne Eintrag: leere Liste', () => {
    expect(anonKeys('NONE')).toEqual([]);
    expect(draftKeys('NONE')).toEqual([]);
  });
});

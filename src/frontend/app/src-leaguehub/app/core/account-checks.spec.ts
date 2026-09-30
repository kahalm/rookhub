import { checkStatusLabel, checkSymbol, checksSummary } from './account-checks';
import { AccountCheckItem } from './league.models';

const I = (status: AccountCheckItem['status']): AccountCheckItem => ({ key: status + Math.random(), label: 'x', status, text: 'y' });

describe('account-checks', () => {
  it('Zeichen und Worte je Ergebnis', () => {
    expect(['ok', 'warn', 'fail', 'none', 'info'].map(s => checkSymbol(s as AccountCheckItem['status']))).toEqual(['✓', '!', '✕', '–', 'i']);
    expect(checkStatusLabel('ok')).toBe('spricht dafür');
    expect(checkStatusLabel('fail')).toBe('spricht dagegen');
    expect(checkStatusLabel('none')).toBe('nichts zu prüfen');
  });

  it('Zusammenfassung zählt dafür, stutzig, dagegen — Einzahl und Mehrzahl', () => {
    expect(checksSummary([I('ok'), I('ok'), I('warn'), I('fail'), I('none'), I('info')]))
      .toBe('2 sprechen dafür, 1 macht stutzig, 1 spricht dagegen.');
    expect(checksSummary([I('ok'), I('warn'), I('warn')])).toBe('1 spricht dafür, 2 machen stutzig.');
    expect(checksSummary([I('none'), I('info')])).toBe('Nichts, was dafür oder dagegen spricht.');
  });
});

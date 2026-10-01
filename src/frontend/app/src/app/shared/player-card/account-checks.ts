import { AccountCheckItem } from '@lh/core/league.models';

/** Zeichen je Ergebnis — Farbe allein trennt nicht für jeden, deshalb auch ein eigenes Zeichen. */
export function checkSymbol(status: AccountCheckItem['status']): string {
  switch (status) {
    case 'ok': return '✓';
    case 'weak': return '(✓)';
    case 'warn': return '!';
    case 'fail': return '✕';
    case 'info': return 'i';
    default: return '–';
  }
}

/** Ergebnis in Worten (für Vorleser und den Tooltip). */
export function checkStatusLabel(status: AccountCheckItem['status']): string {
  switch (status) {
    case 'ok': return 'spricht dafür';
    case 'weak': return 'spricht schwächer dafür';
    case 'warn': return 'macht stutzig';
    case 'fail': return 'spricht dagegen';
    case 'info': return 'zur Kenntnis';
    default: return 'nichts zu prüfen';
  }
}

/** „4 sprechen dafür, 2 schwächer dafür, 1 macht stutzig, 1 spricht dagegen" — was nicht geprüft werden konnte, zählt nicht mit. */
export function checksSummary(items: AccountCheckItem[]): string {
  const n = (s: AccountCheckItem['status']) => items.filter(i => i.status === s).length;
  const ok = n('ok'), weak = n('weak'), warn = n('warn'), fail = n('fail');
  const parts = [
    ok ? `${ok} ${ok === 1 ? 'spricht' : 'sprechen'} dafür` : null,
    weak ? `${weak} schwächer dafür` : null,
    warn ? `${warn} ${warn === 1 ? 'macht' : 'machen'} stutzig` : null,
    fail ? `${fail} ${fail === 1 ? 'spricht' : 'sprechen'} dagegen` : null,
  ].filter(x => !!x);
  return parts.length ? parts.join(', ') + '.' : 'Nichts, was dafür oder dagegen spricht.';
}

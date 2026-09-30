/** Die Seiten, die LeagueHub kennt — SPIEGEL von `LeagueOnlineSites.All` (Kürzel + Anzeige). */
export const ACCOUNT_SITES: { key: string; label: string }[] = [
  { key: 'lichess', label: 'Lichess' },
  { key: 'chess.com', label: 'chess.com' },
];

export const siteLabel = (site: string | null): string => ACCOUNT_SITES.find(s => s.key === site)?.label ?? site ?? '';

/** Anzeige eines Kontos, dessen Seite und Name verborgen bleiben (Minderjährige, 0.610.0). */
export const HIDDEN_ACCOUNT = 'Online-Konto (verborgen – minderjährig)';

/** Absage des Servers beim Anlegen/Ändern als Satz. */
export function accountErrorText(reason: string | undefined): string {
  switch (reason) {
    case 'invalidSite': return 'Unbekannte Seite — Lichess oder chess.com.';
    case 'invalidUser': return 'Das ist kein gültiger Kontoname (und keine Profiladresse).';
    case 'duplicate': return 'Dieses Konto steht schon da.';
    case 'tooMany': return 'Mehr als 20 Konten je Spieler gehen nicht.';
    case 'unknownPlayer': return 'Diesen Spieler kennt LeagueHub nicht.';
    default: return 'Speichern hat nicht geklappt.';
  }
}

/**
 * Links aus dem Turnierverlauf nach draussen: FIDE-Profil des Spielers und die Spielerkarte auf
 * chess-results.
 *
 * <p><b>Die Spielerkarte</b> (`art=9&snr=<Startnummer>`) ist dieselbe Seite, die der Crawler fuer
 * Punkte und Performance liest (`CrawlerService.FetchPlayerCardAsync`). Dort rechnet chess-results
 * die Elo-Aenderung Partie fuer Partie vor — auch bei einem laufenden Turnier, also die
 * „Elo-Vorausschau". Ohne Startnummer gibt es keinen Weg zur Karte; dann bleibt die Turnierseite.</p>
 *
 * <p>`lan=0` (Deutsch) wie die uebrigen chess-results-Links der Turnierseite.</p>
 */

/** Nur Ziffern gelten als Kennung — „0" ist bei chess-results „keine Nummer". */
function cleanId(id: string | null | undefined): string | null {
  const text = (id ?? '').trim();
  return /^\d+$/.test(text) && !/^0+$/.test(text) ? text : null;
}

/** FIDE-Profil (`ratings.fide.com/profile/<Kennung>`) — `null` ohne gueltige Kennung. */
export function fideProfileUrl(fideId: string | null | undefined): string | null {
  const id = cleanId(fideId);
  return id === null ? null : `https://ratings.fide.com/profile/${id}`;
}

/**
 * Die Spielerkarte mit der Elo-Berechnung; ohne Startnummer die Turnierseite selbst. `null` nur
 * ohne gueltige Turnierkennung.
 */
export function chessResultsCardUrl(
  tournamentId: string | null | undefined, startNumber: number | null | undefined,
): string | null {
  const tnr = cleanId(tournamentId);
  if (tnr === null) return null;
  const base = `https://chess-results.com/tnr${tnr}.aspx?lan=0`;
  return startNumber != null && Number.isInteger(startNumber) && startNumber > 0
    ? `${base}&art=9&snr=${startNumber}`
    : base;
}

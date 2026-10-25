/** Antwort der Anmelde-Endpunkte und zugleich die gespeicherte Sitzung (`localStorage` `rookhub_user`). */
export interface AuthResponse {
  token: string;
  username: string;
  userId: number;
  isAdmin: boolean;
  /** Gesetzt, wenn dieses Token via Admin-„Als Nutzer einsteigen" erzeugt wurde. */
  impersonating?: boolean;
  /** Benutzername des Admins, der eingestiegen ist (nur bei impersonating). */
  impersonatorUsername?: string;
  /**
   * Nur im Client gesetzt: diese Anmeldung wurde aus der GETEILTEN Anmeldung einer anderen Oberflaeche
   * uebernommen (Cookie auf der Elterndomaene) und endet mit ihr — `HandoffService.verifyAdoptedSession`
   * gleicht sie beim Start und beim Zurueckkehren in den Tab ab. Eine eigene Anmeldung (Maske,
   * Registrierung) und ein frisches Token nach „Passwort aendern" tragen das Feld nicht.
   */
  adopted?: boolean;
}

/**
 * Den Payload-Teil eines JWT lesen (base64url → JSON). Wirft bei einem unlesbaren Token — die Aufrufer entscheiden,
 * was das heißt (abgelaufen bzw. keine Rechte).
 */
export function jwtPayload(token: string): Record<string, unknown> {
  const base64 = token.split('.')[1].replace(/-/g, '+').replace(/_/g, '/');
  return JSON.parse(atob(base64));
}

/** Ist das Token abgelaufen? Ein unlesbares Token gilt als abgelaufen. */
export function isTokenExpired(token: string): boolean {
  try {
    const payload = jwtPayload(token);
    return !!payload['exp'] && (payload['exp'] as number) * 1000 < Date.now();
  } catch {
    return true; // unparsebares Token -> als abgelaufen behandeln
  }
}

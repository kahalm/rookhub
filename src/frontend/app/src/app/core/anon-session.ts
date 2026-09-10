/**
 * Die Kennung eines Besuchers OHNE Konto — der Ersatz für die User-Id in allen anonymen Pfaden
 * (Puzzle-Versuche, Endless-Fortschritt, Punktepartie).
 *
 * Zwei Dinge, die hier zusammenkommen müssen:
 *
 * 1. **Der Server verlangt UUID-Form** (`ValidationConstants.SessionIdPattern`: Hex + Bindestrich,
 *    32–36 Zeichen). Die Mindestlänge ist keine Formsache: anonyme Daten sind NUR über diese
 *    Kennung getrennt, ein kurzer oder erratbarer Wert wäre der Weg in fremde Sitzungen. Alles,
 *    was hier entsteht, muss also durch dieses Muster passen.
 * 2. **`crypto.randomUUID` gibt es nicht überall.** Es hängt an einem SICHEREN Kontext und ist auf
 *    dem HTTP-Dev-Stack schlicht `undefined`. `crypto.getRandomValues` dagegen gibt es AUCH dort —
 *    daher 16 Zufallsbytes als 32 Hex-Zeichen, was dasselbe Muster erfüllt und dieselbe Entropie
 *    trägt. Erst wenn auch das fehlt, bleibt `Math.random` (schwächer, aber besser als keine Id;
 *    die Form stimmt weiterhin).
 */
export function newAnonSessionId(): string {
  const uuid = globalThis.crypto?.randomUUID?.();
  if (uuid) return uuid;

  const bytes = new Uint8Array(16);
  if (globalThis.crypto?.getRandomValues) {
    globalThis.crypto.getRandomValues(bytes);
  } else {
    for (let i = 0; i < bytes.length; i++) bytes[i] = Math.floor(Math.random() * 256);
  }
  return Array.from(bytes, b => b.toString(16).padStart(2, '0')).join('');
}

/**
 * Kennung aus dem `localStorage` holen oder vergeben. `memory` ist die Rückfallebene für Browser
 * mit gesperrten Site-Daten (Privatmodus): dort WIRFT schon der Zugriff, und ohne diesen Fang riss
 * jeder anonyme Aufruf mitten im Ablauf ab. Die Kennung gilt dann nur für diesen Seitenaufruf —
 * mehr ist ohne Speicher nicht zu haben.
 */
const memory = new Map<string, string>();

export function getOrCreateAnonSessionId(key: string): string {
  try {
    const stored = localStorage.getItem(key);
    if (stored) return stored;
    const fresh = newAnonSessionId();
    localStorage.setItem(key, fresh);
    return fresh;
  } catch {
    let id = memory.get(key);
    if (!id) {
      id = newAnonSessionId();
      memory.set(key, id);
    }
    return id;
  }
}

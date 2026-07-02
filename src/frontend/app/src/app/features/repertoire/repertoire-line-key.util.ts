import { normSan } from './repertoire-tree.util';

/**
 * Stabiler Linien-Schlüssel = Hash der normalisierten SAN-Zugfolge der ganzen Linie. Muss
 * überall (Trainer + Linienliste) identisch berechnet werden, damit der SR-Zustand einer Linie
 * über beide Ansichten dieselbe Identität hat. Ändert sich die Zugfolge (Re-Import/Edit), gilt die
 * Linie als neu (= noch nicht im Pool) — gewolltes Verhalten.
 */
export function lineKeyFromSans(sans: string[]): string {
  const norm = sans.map(normSan).join(' ');
  return 'l' + cyrb53(norm).toString(36);
}

/** Kompakter, kollisionsarmer 53-bit-String-Hash (deterministisch, kein Krypto-Anspruch). */
function cyrb53(str: string, seed = 0): number {
  let h1 = 0xdeadbeef ^ seed, h2 = 0x41c6ce57 ^ seed;
  for (let i = 0; i < str.length; i++) {
    const ch = str.charCodeAt(i);
    h1 = Math.imul(h1 ^ ch, 2654435761);
    h2 = Math.imul(h2 ^ ch, 1597334677);
  }
  h1 = Math.imul(h1 ^ (h1 >>> 16), 2246822507);
  h1 ^= Math.imul(h2 ^ (h2 >>> 13), 3266489909);
  h2 = Math.imul(h2 ^ (h2 >>> 16), 2246822507);
  h2 ^= Math.imul(h1 ^ (h1 >>> 13), 3266489909);
  return 4294967296 * (2097151 & h2) + (h1 >>> 0);
}

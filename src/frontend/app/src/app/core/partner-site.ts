/**
 * RookHub und die Turnierseite sind zwei Oberflaechen desselben Kontos auf zwei Adressen.
 * Welche die jeweils andere ist, leitet sich aus dem HOST ab statt aus einer Build-Variablen —
 * so stimmt es auf Dev und Prod ohne zwei Konfigurationen:
 *
 *   rookhub.oberschmid.homes      ↔ turnier.oberschmid.homes
 *   rookhub-dev.oberschmid.homes  ↔ turnier-dev.oberschmid.homes
 *
 * Steht die App woanders (localhost, IP, Vorschau-Build), gibt es keinen Partner — der Sprung
 * wird dann gar nicht angeboten, statt auf eine geratene Adresse zu zeigen.
 */
export type SiteKind = 'rookhub' | 'turnier';

/** Erste Label-Komponente des Hosts, sofern sie zu einer der beiden Seiten passt. */
export function siteKindOf(host: string = location.hostname): SiteKind | null {
  const first = host.split('.')[0];
  if (first === 'rookhub' || first === 'rookhub-dev') return 'rookhub';
  if (first === 'turnier' || first === 'turnier-dev') return 'turnier';
  return null;
}

/**
 * Basis-URL der Schwesterseite (ohne abschliessenden Schraegstrich) — `null`, wenn der aktuelle
 * Host keiner der beiden Seiten entspricht.
 */
export function partnerSiteUrl(host: string = location.hostname, protocol: string = location.protocol): string | null {
  const parts = host.split('.');
  const first = parts[0];
  const swap: Record<string, string> = {
    'rookhub': 'turnier', 'rookhub-dev': 'turnier-dev',
    'turnier': 'rookhub', 'turnier-dev': 'rookhub-dev',
  };
  const other = swap[first];
  if (!other || parts.length < 2) return null;
  return `${protocol}//${[other, ...parts.slice(1)].join('.')}`;
}

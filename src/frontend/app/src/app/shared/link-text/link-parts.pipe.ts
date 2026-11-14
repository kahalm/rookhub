import { Pipe, PipeTransform } from '@angular/core';

/** Ein Stück Fließtext: reiner Text oder — mit `href` — eine Webadresse, die als Link ausgegeben wird. */
export interface LinkPart { text: string; href?: string; }

const URL_RE = /https?:\/\/[^\s<>"']+/g;
/** Satzzeichen am Ende gehören zum Satz, nicht zur Adresse („… siehe https://x.at/a.“). */
const TRAILING_RE = /[.,;:!?)\]}»“”’]+$/;

/**
 * Zerlegt einen Text in Text- und Link-Stücke: jede `http(s)://`-Adresse wird ein eigenes Stück mit `href`.
 * Die Vorlage gibt die Stücke per `@for` aus — kein `innerHTML`, der Text bleibt also immer Text (Nachrichten
 * stammen von Nutzern). Andere Schemata (`javascript:`, `data:` …) werden nie zu Links.
 */
export function splitLinks(text: string | null | undefined): LinkPart[] {
  const s = text ?? '';
  const parts: LinkPart[] = [];
  let last = 0;
  for (const m of s.matchAll(URL_RE)) {
    const url = m[0].replace(TRAILING_RE, '');
    const start = m.index ?? 0;
    if (url.length <= 'https://'.length) continue;
    if (start > last) parts.push({ text: s.slice(last, start) });
    parts.push({ text: url, href: url });
    last = start + url.length;
  }
  if (last < s.length) parts.push({ text: s.slice(last) });
  return parts;
}

/** `@for (p of text | linkParts; track $index)` → `p.href ? <a> : Text`. Rein: gleicher Text, gleiche Stücke. */
@Pipe({ name: 'linkParts', standalone: true })
export class LinkPartsPipe implements PipeTransform {
  transform(text: string | null | undefined): LinkPart[] {
    return splitLinks(text);
  }
}

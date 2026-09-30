/**
 * RookHub und die Turnierseite sind zwei Oberflaechen desselben Kontos auf zwei Adressen.
 * Welche die jeweils andere ist, leitet sich aus dem HOST ab statt aus einer Build-Variablen —
 * so stimmt es auf Dev und Prod ohne zwei Konfigurationen:
 *
 *   rookhub.oberschmid.homes      ↔ tournament.oberschmid.homes
 *   rookhub-dev.oberschmid.homes  ↔ turnier-dev.oberschmid.homes
 *
 * <b>Warum die beiden Seiten verschieden heissen:</b> Prod hat die Turnierseite unter
 * `tournament` bekommen, Dev laeuft weiter unter `turnier-dev`. Die Zuordnung steht deshalb als
 * TABELLE da und nicht als Regel — eine Regel muesste sich eine der beiden Schreibweisen
 * ausdenken. Als Turnierseite ERKANNT werden alle vier Schreibweisen, damit ein alter Link
 * (`turnier.…`) weiter funktioniert; wer Dev auf `tournament-dev` umstellt, muss hier nichts
 * aendern ausser dem einen Eintrag.
 *
 * Steht die App woanders (localhost, IP, Vorschau-Build), gibt es keinen Partner — der Sprung
 * wird dann gar nicht angeboten, statt auf eine geratene Adresse zu zeigen.
 */
export type SiteKind = 'rookhub' | 'turnier';

/** Alle Label, unter denen die Turnierseite erreichbar ist. */
const TOURNAMENT_LABELS = new Set(['turnier', 'tournament', 'turnier-dev', 'tournament-dev']);
const ROOKHUB_LABELS = new Set(['rookhub', 'rookhub-dev']);
/**
 * KidHub, die Kinderseite: KEINE Partnerseite (kein Sprung, kein Konto), teilt aber die
 * Anzeige-Einstellungen. Ohne diesen Eintrag las KidHub das Sprach-Cookie, durfte es aber nicht
 * schreiben — eine dort getroffene Wahl verlor beim naechsten Laden gegen das Cookie von RookHub.
 */
const KIDHUB_LABELS = new Set(['kidhub', 'kidhub-dev']);
/** LeagueHub (Aufstellungs-Prognosen der Tiroler Ligen): wie KidHub keine Partnerseite, teilt aber die Anzeige-Einstellungen. */
const LEAGUEHUB_LABELS = new Set(['leaguehub', 'leaguehub-dev']);
/** ClubHub (Kartei der Kinder und Jugendlichen): wie LeagueHub keine Partnerseite, teilt aber die Anzeige-Einstellungen. */
const CLUBHUB_LABELS = new Set(['clubhub', 'clubhub-dev']);

/** Welche Seite gehoert zu welcher — die Tabelle, nicht geraten. */
const PARTNER: Record<string, string> = {
  'rookhub': 'tournament',        // Prod
  'rookhub-dev': 'turnier-dev',   // Dev
  'tournament': 'rookhub',
  'turnier': 'rookhub',
  'tournament-dev': 'rookhub-dev',
  'turnier-dev': 'rookhub-dev',
};

/** Erste Label-Komponente des Hosts, sofern sie zu einer der beiden Seiten passt. */
export function siteKindOf(host: string = location.hostname): SiteKind | null {
  const first = host.split('.')[0];
  if (ROOKHUB_LABELS.has(first)) return 'rookhub';
  if (TOURNAMENT_LABELS.has(first)) return 'turnier';
  return null;
}

/**
 * Basis-URL der Schwesterseite (ohne abschliessenden Schraegstrich) — `null`, wenn der aktuelle
 * Host keiner der beiden Seiten entspricht.
 */
export function partnerSiteUrl(host: string = location.hostname, protocol: string = location.protocol): string | null {
  const parts = host.split('.');
  const other = PARTNER[parts[0]];
  if (!other || parts.length < 2) return null;
  return `${protocol}//${[other, ...parts.slice(1)].join('.')}`;
}

/** Welches LeagueHub zu welchem RookHub gehört (Prod ↔ Prod, Dev ↔ Dev). */
const LEAGUEHUB_FOR: Record<string, string> = { 'rookhub': 'leaguehub', 'rookhub-dev': 'leaguehub-dev' };

/**
 * Basis-URL von LeagueHub zu diesem RookHub (ohne abschließenden Schrägstrich) — für den Sprung „In die
 * Vereins-Datenbank" auf der Partieseite. `null` außerhalb von RookHub (localhost, IP, andere Seiten): dann gibt es den
 * Menüpunkt nicht, statt auf eine geratene Adresse zu zeigen.
 */
export function leagueHubUrl(host: string = location.hostname, protocol: string = location.protocol): string | null {
  const parts = host.split('.');
  const other = LEAGUEHUB_FOR[parts[0]];
  if (!other || parts.length < 2) return null;
  return `${protocol}//${[other, ...parts.slice(1)].join('.')}`;
}

/** Der Rückweg: welches RookHub zu diesem LeagueHub gehört (für Links auf „Meine Partien"). */
export function rookHubUrlForLeagueHub(host: string = location.hostname, protocol: string = location.protocol): string | null {
  const parts = host.split('.');
  const other = Object.entries(LEAGUEHUB_FOR).find(([, lh]) => lh === parts[0])?.[0];
  if (!other || parts.length < 2) return null;
  return `${protocol}//${[other, ...parts.slice(1)].join('.')}`;
}

/**
 * Domaene fuer Cookies, die sich die Oberflaechen teilen sollen (z. B. Design-Modus, Sprache — KidHub, LeagueHub und ClubHub eingeschlossen):
 * `.oberschmid.homes` fuer `rookhub-dev.oberschmid.homes`. `null`, wenn der Host keine der beiden
 * Seiten ist — auf einer IP oder localhost gibt es keine gemeinsame Elterndomaene, und ein Cookie
 * darauf zu setzen wuerde stillschweigend nichts tun.
 */
export function sharedCookieDomain(host: string = location.hostname): string | null {
  const first = host.split('.')[0];
  if (!siteKindOf(host) && !KIDHUB_LABELS.has(first) && !LEAGUEHUB_LABELS.has(first) && !CLUBHUB_LABELS.has(first)) return null;
  const parts = host.split('.');
  return parts.length >= 2 ? '.' + parts.slice(1).join('.') : null;
}

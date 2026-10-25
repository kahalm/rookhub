import type { PathOptions } from 'leaflet';
import type { MapPinMarker } from './map-pin-marker';
import { DirectoryEntry, DirectoryVenue, isPlaceholderLocation } from './tournament-directory.model';
import { MIXED, PinCategory } from './pin-palette';

/*
 * Die Punkte der Turnierkarte: welche Turniere auf welcher Koordinate zusammenliegen und wie ein
 * solcher Punkt aussieht. Rein (ohne Leaflet-Instanz und ohne Angular) — die Karte
 * (`TournamentMapComponent`) zeichnet nur, was hier entschieden wird.
 */

/**
 * Ein Punkt und alles, was auf ihm liegt. Der Pin gehoert der KOORDINATE, nicht dem Turnier —
 * zwei Turniere mit demselben Ortstext („Tirol") sitzen auf derselben Landesmitte, und der
 * zweite Pin waere unter dem ersten unerreichbar.
 */
export interface PinGroup {
  lat: number;
  lon: number;
  members: PinMember[];
  marker?: MapPinMarker;
  /** Die gemeinsame Klasse des gewaehlten Merkmals; `MIXED`, wenn sie sich unterscheiden. */
  category: PinCategory;
}

/** Ein Turnier an diesem Punkt samt dem Spielort, mit dem es hierher kam. */
export interface PinMember {
  entry: DirectoryEntry;
  spot: DirectoryVenue;
}

/**
 * Die zu zeichnenden Punkte, gebuendelt nach Koordinate und in Zeichenreihenfolge.
 *
 * <p>Gebuendelt wird bei EXAKT gleicher Koordinate (auf fuenf Nachkommastellen, gut einen Meter),
 * nicht bei „nah beieinander": ein Abstandsmass haenge am Zoom und wuerde zwei verschiedene Orte
 * zu einem erklaeren, sobald man weit genug herauszoomt. Gleiche Koordinate heisst dagegen
 * nachweislich dieselbe Aussage.</p>
 *
 * <p>Gemerkte ZULETZT: Leaflet zeichnet in Reihenfolge des Hinzufuegens, und ein gemerkter Punkt
 * soll nicht unter einem beliebigen anderen liegen. In einer Stadt mit dreissig Turnieren ist
 * genau das der Unterschied zwischen „ich sehe meins" und „ich suche meins". `sort` ist stabil,
 * die Reihenfolge des Servers bleibt innerhalb der beiden Gruppen also erhalten.</p>
 */
export function groupByPoint(entries: DirectoryEntry[]): PinGroup[] {
  const byPoint = new Map<string, PinGroup>();
  for (const entry of entries) {
    // Ein Punkt JE SPIELORT: bei Ligen nennt chess-results mehrere („Mayrhofen, St.Veit").
    // Mit nur dem Hauptort verschwaende ein Turnier die Haelfte seiner Orte, und die
    // Umkreissuche fand es nicht, obwohl es zur Haelfte vor der Haustuer stattfindet.
    for (const spot of venuesOf(entry)) {
      const key = `${spot.lat.toFixed(5)}|${spot.lon.toFixed(5)}`;
      const group = byPoint.get(key);
      if (group) group.members.push({ entry, spot });
      else byPoint.set(key,
        { lat: spot.lat, lon: spot.lon, members: [{ entry, spot }], category: MIXED });
    }
  }
  return [...byPoint.values()]
    .sort((a, b) => Number(hasSubscribed(a)) - Number(hasSubscribed(b)));
}

/** Gemerkt ist ein Punkt, sobald EINES seiner Turniere gemerkt ist — sonst ginge es unter. */
export function hasSubscribed(group: PinGroup): boolean {
  return group.members.some(m => m.entry.subscribed);
}

/** Nur ungefaehr verortet ist ein Punkt, wenn ALLE seine Spielorte Regionsmitten sind. */
export function isVague(group: PinGroup): boolean {
  return group.members.every(m => m.spot.geoSource === 'Region');
}

/** Der Ortsname des Punktes; alle seine Spielorte liegen ja auf derselben Koordinate. */
export function placeOf(group: PinGroup): string {
  const { entry, spot } = group.members[0];
  // Ein Platzhalter wie „various" ist kein Ortsname (r2-t-various) — dann lieber keiner.
  return spot.name || entry.geoPlaceName || (isPlaceholderLocation(entry.location) ? '' : entry.location) || '';
}

export function groupStyle(group: PinGroup): PathOptions {
  return pinStyle(group.category, hasSubscribed(group), isVague(group));
}

/**
 * Wie ein Punkt aussieht. Zwei Angaben stecken darin, und beide muessen ohne Legende ablesbar
 * sein:
 *
 * <p><b>Die KLASSE des gewaehlten Merkmals</b> traegt die FUELLUNG (Bedenkzeit, Turnierart,
 * Nachwuchs, Geschlechtsklasse — siehe `pin-palette`). Das ist der Kanal, den man ueberfliegen
 * kann: „wo ist ein Schnellschachturnier" beantwortet sich damit ohne einen einzigen Klick.</p>
 *
 * <p><b>Gemerkt</b> — ein dicker, fast schwarzer RING. Frueher war das die Fuellung (Amber), aber
 * die traegt jetzt die Klasse, und zwei Bedeutungen auf einem Kanal sind keine Auskunft. Der Ring
 * ist dafuer der richtige Platz: „gemerkt" ist eine Ja/Nein-Angabe, und ein Rand um eine beliebige
 * Fuellung bleibt in jeder Farbe erkennbar. Gemerkte werden ausserdem ZULETZT gezeichnet und
 * liegen damit oben.</p>
 *
 * <p><b>Nur ungefaehr verortet</b> (Bundesland-Mittelpunkt) — sichtbar abgeschwaecht, sonst
 * suggeriert ein knackiger Pin eine Genauigkeit, die er nicht hat. Diese Abschwaechung gilt auch
 * fuer gemerkte, damit die Aussage nicht verlorengeht.</p>
 *
 * <p>Die vierte Angabe — wie VIELE Turniere auf dem Punkt liegen — traegt der Pin als Zahl im
 * Kopf (siehe `MapPinMarker`), nicht als weitere Farbe: eine Anzahl ist eine Anzahl.</p>
 */
export function pinStyle(category: PinCategory, subscribed: boolean, vague: boolean): PathOptions {
  return {
    color: subscribed ? '#202124' : category.stroke,
    fillColor: category.fill,
    weight: subscribed ? 4 : 2,
    fillOpacity: vague ? 0.45 : 0.85,
  };
}

/**
 * Die zu zeichnenden Punkte eines Turniers: die Liste der Spielorte, wenn es mehrere gibt, sonst
 * der eine Ort am Eintrag selbst. Nicht verortete Turniere liefern nichts.
 */
export function venuesOf(entry: DirectoryEntry): DirectoryVenue[] {
  if (entry.venues.length > 0) return entry.venues;
  if (entry.lat == null || entry.lon == null) return [];
  return [{ name: entry.geoPlaceName ?? entry.location ?? '', lat: entry.lat, lon: entry.lon,
            geoSource: entry.geoSource }];
}

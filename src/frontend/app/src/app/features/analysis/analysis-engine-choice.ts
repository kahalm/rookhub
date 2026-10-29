import type { EngineChoice } from './analysis-engine-card.component';
import { engineTagKey, ExternalEngineInfo, ExternalEnginesResponse } from './external-engine.service';
import { formatCount } from './engine-lines.util';

/**
 * Engine-Wahl des Analysebretts, rein (Zerlegung Z22): welche Engine nach dem Laden der Liste gilt, die Liste der
 * wählbaren Engines (memoisiert) und der Tempo-Text hinter dem (i).
 */

/** Ergebnis der geladenen Engine-Liste. */
export interface EnginePick {
  /** Live-Picker: alle externen ohne die Hintergrund-Engines — außer der eines Auftrags. */
  list: ExternalEngineInfo[];
  backgroundEngineIds: string[];
  /** Mindestens eine externe Engine registriert (inkl. Hintergrund-Engine) → Aufträge sind möglich. */
  hasExternalEngines: boolean;
  /** Zu wählende Engine (null = Auswahl bleibt). */
  selected: string | null;
  /** Die Wahl kam vom Auftrag (`?engine=`) — gilt nur für diesen Aufruf. */
  fromRequest: boolean;
}

/**
 * Die Hintergrund-Engine gehört den Aufträgen — im Live-Picker (und im Vergleich) taucht sie nicht auf, sonst
 * konkurrierten zwei Suchen um dieselben Kerne. Kam der Aufruf von einem Auftrag („im Analysebrett öffnen"), gilt
 * DESSEN Engine — auch die Hintergrund-Engine; der Provider hat die Stellung noch im Hash, die Suche ist sofort wieder
 * auf der erreichten Tiefe. Sonst die zuletzt gewählte, wenn es sie noch gibt.
 */
export function pickEngineAfterLoad(r: ExternalEnginesResponse, requested: string | null, stored: string | null): EnginePick {
  const backgroundEngineIds = r.backgroundEngineIds ?? [];
  let list = r.engines.filter(e => !backgroundEngineIds.includes(e.id));
  const base = { backgroundEngineIds, hasExternalEngines: r.engines.length > 0 };
  if (requested && r.engines.some(e => e.id === requested)) {
    if (!list.some(e => e.id === requested)) list = [...list, r.engines.find(e => e.id === requested)!];
    return { ...base, list, selected: requested, fromRequest: true };
  }
  const restore = stored && stored !== 'wasm' && list.some(e => e.id === stored) ? stored : null;
  return { ...base, list, selected: restore, fromRequest: false };
}

/**
 * Alle wählbaren Engines (Browser + registrierte externe) — für beide Auswahlfelder, memoisiert.
 * Template-gebunden in einer Default-Change-Detection-Komponente, die unter einer externen Engine viele Male pro
 * Sekunde markiert wird: unmemoisiert entstand bei JEDEM Durchlauf ein frisches Array frischer Objekte samt
 * Übersetzungs-Nachschlag. Der Cache hält die Sprache mit fest, sonst bliebe die Beschriftung nach einem
 * Sprachwechsel auf der alten stehen.
 */
export class EngineChoiceList {
  private cache?: { lang: string | null; list: ExternalEngineInfo[]; value: EngineChoice[] };

  get(lang: string | null, list: ExternalEngineInfo[], browserName: () => string): EngineChoice[] {
    const c = this.cache;
    if (c && c.lang === lang && c.list === list) return c.value;
    const value = [
      { id: 'wasm', name: browserName(), tag: null },
      // `tag`: „· über Lichess" / „· offline" hinter dem Namen — die Auswahl mischt beide Quellen.
      ...list.map(e => ({ id: e.id, name: e.name, tag: engineTagKey(e) })),
    ];
    this.cache = { lang, list, value };
    return value;
  }
}

/** Tempo-Text hinter dem (i); `nodes === null` = Kurzform (Vergleichs-Engine). Template-gebunden — darf nie werfen,
 *  deshalb `toLocaleString` mit Rückfall statt Angulars formatNumber (NG0701 bei fehlenden Locale-Daten). */
export function speedHintText(t: (key: string, params?: object) => string, locale: string, nps: number,
                              nodes: number | null): string {
  if (nps <= 0) return t('analysis.speedWaiting');
  // Volle Zahlen mit Tausenderpunkt statt „kN/s“ — die Erklärung arbeitete sonst mit der Einheit, die sie erklären soll
  // (d8-rk-engine-tooltip-kn-jargon, DESIGN 2 Nr. 7). Die Einheit „Stellungen pro Sekunde“ steht im Text.
  const speed = formatCount(nps, locale);
  return nodes === null
    ? t('analysis.speedShort', { speed })
    : t('analysis.speedHint', { speed, nodes: formatCount(nodes, locale) });
}

import { formatTournamentDates } from '../../core/tournament-date';
import { PinGroup, PinMember, isVague, placeOf } from './map-pin-groups';
import { DirectoryEntry, displayLocation } from './tournament-directory.model';

/** Uebersetzt einen Schluessel — in der Karte `TranslateService.instant`. */
export type MapText = (key: string, params?: Record<string, unknown>) => string;

/**
 * Der DOM-Bau von Hover-Hinweis und Buendel-Liste der Turnierkarte, als reine Funktionen: Leaflet
 * haelt beides in eigenen Containern ausserhalb des Templates der Karte, gebaut wird also von
 * Hand. Was hier NICHT steht, ist die Kurzansicht eines einzelnen Turniers — die ist eine
 * Angular-Komponente und entsteht in `TournamentMapComponent.buildCard` ueber den ViewContainerRef.
 */
export const MapPopupBuilder = {
  /**
   * Der Hinweis am Pin. Bei einem Buendel nennt er die Anzahl statt eines der Turniere — ein
   * herausgegriffener Name behauptete, der Punkt gehoere ihm.
   */
  tooltipHtml(group: PinGroup, text: MapText, locale: string | null): string {
    if (group.members.length > 1) {
      const place = placeOf(group);
      return `<strong>${escapeHtml(
        text('tournamentDirectory.map.samePoint', { count: group.members.length }))}</strong>` +
        (place ? `<br>${escapeHtml(place)}` : '');
    }
    const { entry, spot } = group.members[0];
    // „various" ist kein Ort: uebersetzt (UI-Sweep 2026-10-11, r2-t-various).
    const place = entry.venues.length > 1 ? spot.name : displayLocation(entry.location, key => text(key));
    const when = dateRange(entry, locale);
    return `<strong>${escapeHtml(entry.name)}</strong><br>${escapeHtml(when)}` +
           (place ? `<br>${escapeHtml(place)}` : '');
  },

  /**
   * Die Liste aller Turniere eines Punktes als Knoten (Kopf, Ort, ggf. Begruendung, Zeilen); ein
   * Klick auf eine Zeile ruft `onPick` mit dem Turnier.
   */
  groupList(group: PinGroup, text: MapText, locale: string | null,
            onPick: (member: PinMember) => void): HTMLElement[] {
    const nodes: HTMLElement[] = [];
    nodes.push(paragraph('tm-group-head',
      text('tournamentDirectory.map.samePoint', { count: group.members.length })));

    const place = placeOf(group);
    if (place) nodes.push(paragraph('tm-group-place', place));

    // Warum sie uebereinanderliegen, sagt sich nur bei der einen Ursache, die wir kennen: alle
    // Spielorte dieses Punktes sind Regionsmitten. Ohne diesen Satz sieht es nach einem Fehler
    // aus, obwohl es die ehrliche Wiedergabe eines Ortstextes ist, der nur „Tirol" sagt.
    if (isVague(group)) nodes.push(paragraph('tm-group-vague', text('tournamentDirectory.map.samePointVague')));

    const list = document.createElement('div');
    list.className = 'tm-group-list';
    for (const member of group.members) {
      const row = document.createElement('button');
      row.type = 'button';
      row.className = 'tm-group-row';
      if (member.entry.subscribed) row.classList.add('is-marked');

      const name = document.createElement('span');
      name.className = 'tm-group-name';
      // textContent, nicht innerHTML: die Namen kommen von chess-results, also von aussen.
      name.textContent = member.entry.name;
      const when = document.createElement('span');
      when.className = 'tm-group-when';
      when.textContent = dateRange(member.entry, locale);

      row.append(name, when);
      row.addEventListener('click', () => onPick(member));
      list.appendChild(row);
    }
    nodes.push(list);
    return nodes;
  },

  /** Der Weg aus der Kurzansicht eines Buendel-Turniers zurueck zur Liste. */
  backButton(group: PinGroup, text: MapText, onBack: () => void): HTMLButtonElement {
    const back = document.createElement('button');
    back.type = 'button';
    back.className = 'tm-group-back';
    back.textContent = text('tournamentDirectory.map.backToList', { count: group.members.length });
    back.addEventListener('click', onBack);
    return back;
  },
};

function paragraph(className: string, content: string): HTMLParagraphElement {
  const p = document.createElement('p');
  p.className = className;
  p.textContent = content;
  return p;
}

/**
 * „18.–20. Dez. 2026" bzw. nur der eine Tag, in der Oberflaechensprache; leer, wenn chess-results
 * gar kein Datum lieferte (Codereview F6-010: bis dahin roh „2026-12-18 – 2026-12-20").
 */
function dateRange(entry: DirectoryEntry, locale: string | null): string {
  return formatTournamentDates(entry.startDate, entry.endDate, locale);
}

/** Turnier- und Ortsnamen kommen von chess-results — also fremder Text in einem innerHTML-Tooltip. */
export function escapeHtml(value: string): string {
  return value
    .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;').replace(/'/g, '&#39;');
}

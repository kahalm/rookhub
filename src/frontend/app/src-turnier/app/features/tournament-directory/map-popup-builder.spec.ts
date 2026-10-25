import { DirectoryEntry } from './tournament-directory.model';
import { groupByPoint } from './map-pin-groups';
import { MapPopupBuilder, MapText, escapeHtml } from './map-popup-builder';

function entry(over: Partial<DirectoryEntry> = {}): DirectoryEntry {
  return {
    id: '1', chessResultsId: '1', name: 'Turnier', federation: 'AUT', state: null,
    startDate: '2026-10-10', endDate: '2026-10-12', location: 'Salzburg', timeControl: null,
    speed: 'Standard', organizer: null, director: null, chiefArbiter: null,
    rounds: null, playerCount: null, lat: 47.8, lon: 13.04, geoSource: 'City', geoPlaceName: null,
    distanceKm: null, cancelled: false, subscribed: false, groupSize: 1, groups: [], venues: [],
    kind: 'Individual', isLeague: false, ageGroups: [], gender: 'Open',
    ignored: false, roundDates: [], sources: [], ...over,
  };
}

/** Gibt Schluessel und Parameter zurueck — so ist sichtbar, WAS uebersetzt wurde. */
const text: MapText = (key, params) => (params ? `${key}${JSON.stringify(params)}` : key);

const spaces = (s: string | null | undefined) => (s ?? '').replace(/\s/g, ' ');

describe('MapPopupBuilder', () => {
  it('baut den Hinweis eines einzelnen Turniers aus Name, Termin und Ort', () => {
    const [group] = groupByPoint([entry({ name: 'Open <b>' })]);

    expect(spaces(MapPopupBuilder.tooltipHtml(group, text, 'en')))
      .toBe('<strong>Open &lt;b&gt;</strong><br>Oct 10 – 12, 2026<br>Salzburg');
  });

  it('übersetzt einen Platzhalter-Ort und nimmt bei mehreren Spielorten den des Punktes', () => {
    const [platzhalter] = groupByPoint([entry({ location: 'various' })]);
    expect(MapPopupBuilder.tooltipHtml(platzhalter, text, 'en'))
      .toContain('<br>tournamentDirectory.variousVenues');

    const [ersterOrt] = groupByPoint([entry({
      location: 'various',
      venues: [
        { name: 'Mayrhofen', lat: 47.1667, lon: 11.8667, geoSource: 'City' },
        { name: 'St.Veit', lat: 46.7667, lon: 14.3667, geoSource: 'City' },
      ],
    })]);
    expect(MapPopupBuilder.tooltipHtml(ersterOrt, text, 'en')).toMatch(/<br>Mayrhofen$/);
  });

  it('lässt Termin- und Ortszeile weg, wenn sie leer sind — der Umbruch für den Termin bleibt', () => {
    const [group] = groupByPoint([entry({ startDate: null, endDate: null, location: null })]);

    expect(MapPopupBuilder.tooltipHtml(group, text, 'en')).toBe('<strong>Turnier</strong><br>');
  });

  it('nennt bei einem Bündel die Anzahl und den Ort statt eines Namens', () => {
    const [group] = groupByPoint([entry(), entry({ id: '2' })]);

    expect(MapPopupBuilder.tooltipHtml(group, text, 'en'))
      .toBe('<strong>tournamentDirectory.map.samePoint{&quot;count&quot;:2}</strong><br>Salzburg');
  });

  // P15/4e: zwei Turniere ohne Spielort-Liste, beide mit Ortstext „various" — Hinweis und
  // Bündel-Kopf nannten „various" als Ort.
  it('nennt bei einem Bündel keinen Platzhalter als Ort', () => {
    const [group] = groupByPoint([entry({ location: 'various' }), entry({ id: '2', location: 'various' })]);

    expect(MapPopupBuilder.tooltipHtml(group, text, 'en'))
      .toBe('<strong>tournamentDirectory.map.samePoint{&quot;count&quot;:2}</strong>');
    const nodes = MapPopupBuilder.groupList(group, text, 'en', () => {});
    expect(nodes.some(n => n.classList.contains('tm-group-place'))).toBeFalse();
    expect(nodes.map(n => n.textContent).join()).not.toContain('various');
  });

  it('baut die Liste eines Bündels: Kopf, Ort, Begründung und eine Zeile je Turnier', () => {
    const [group] = groupByPoint([
      entry({ name: 'A', geoSource: 'Region' }),
      entry({ id: '2', name: 'B', geoSource: 'Region', subscribed: true }),
    ]);
    const picked: string[] = [];

    const nodes = MapPopupBuilder.groupList(group, text, 'en', m => picked.push(m.entry.id));

    expect(nodes.map(n => n.className))
      .toEqual(['tm-group-head', 'tm-group-place', 'tm-group-vague', 'tm-group-list']);
    expect(nodes[0].textContent).toBe('tournamentDirectory.map.samePoint{"count":2}');
    expect(nodes[1].textContent).toBe('Salzburg');
    expect(nodes[2].textContent).toBe('tournamentDirectory.map.samePointVague');

    const rows = [...nodes[3].querySelectorAll<HTMLButtonElement>('button.tm-group-row')];
    expect(rows.map(r => r.querySelector('.tm-group-name')!.textContent)).toEqual(['A', 'B']);
    expect(rows.map(r => spaces(r.querySelector('.tm-group-when')!.textContent)))
      .toEqual(['Oct 10 – 12, 2026', 'Oct 10 – 12, 2026']);
    expect(rows.map(r => r.classList.contains('is-marked'))).toEqual([false, true]);
    expect(rows.every(r => r.type === 'button')).toBeTrue();

    rows[1].click();
    expect(picked).toEqual(['2']);
  });

  it('lässt Ort und Begründung weg, wenn es sie nicht gibt', () => {
    const [group] = groupByPoint([
      entry({ location: null, geoSource: 'City' }),
      entry({ id: '2', location: null, geoSource: 'Region' }),
    ]);

    expect(MapPopupBuilder.groupList(group, text, 'en', () => undefined).map(n => n.className))
      .toEqual(['tm-group-head', 'tm-group-list']);
  });

  it('nimmt Namen als Text, nicht als HTML', () => {
    const [group] = groupByPoint([entry({ name: '<img src=x>' }), entry({ id: '2' })]);
    const list = MapPopupBuilder.groupList(group, text, 'en', () => undefined).at(-1)!;

    expect(list.querySelector('img')).toBeNull();
    expect(list.querySelector('.tm-group-name')!.textContent).toBe('<img src=x>');
  });

  it('baut den Weg zurück zur Liste mit der Anzahl', () => {
    const [group] = groupByPoint([entry(), entry({ id: '2' }), entry({ id: '3' })]);
    let back = 0;

    const button = MapPopupBuilder.backButton(group, text, () => back++);
    button.click();

    expect(button.className).toBe('tm-group-back');
    expect(button.type).toBe('button');
    expect(button.textContent).toBe('tournamentDirectory.map.backToList{"count":3}');
    expect(back).toBe(1);
  });

  it('maskiert alle fünf HTML-Sonderzeichen', () => {
    expect(escapeHtml(`<a href="x">'&'</a>`))
      .toBe('&lt;a href=&quot;x&quot;&gt;&#39;&amp;&#39;&lt;/a&gt;');
  });
});

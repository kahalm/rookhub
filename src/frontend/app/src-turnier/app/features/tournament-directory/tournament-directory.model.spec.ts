import { DirectoryEntry, directoryPageSize, displayLocation, groupSameName, isPlaceholderLocation, sameNameSiblings } from './tournament-directory.model';

/** UI-Sweep 2026-10-11 (r2-t-various, r2-t-dup-cards): reine Helfer der Turnierkarten. */
describe('tournament-directory.model', () => {
  // Falz-Runde 16.10.: am Handy 25 Turniere je Abruf (50 Karten waren 12,8 Bildschirme), breit weiter 50
  it('Seitengröße der Liste: Handy 25, sonst 50; ohne Angabe nach der Fensterbreite', () => {
    expect(directoryPageSize(true)).toBe(25);
    expect(directoryPageSize(false)).toBe(50);
    expect(directoryPageSize()).toBe(matchMedia('(max-width: 599.98px)').matches ? 25 : 50);
  });

  it('erkennt Platzhalter statt eines Orts, sonst nicht', () => {
    for (const text of ['various', 'Various', ' various ', 'div.', 'Diverse', 'verschiedene', 'Various venues']) {
      expect(isPlaceholderLocation(text)).withContext(text).toBeTrue();
    }
    for (const text of [null, undefined, '', 'Varnsdorf', 'Diversity Hall', 'Divaca']) {
      expect(isPlaceholderLocation(text)).withContext(String(text)).toBeFalse();
    }
  });

  it('zeigt einen Platzhalter uebersetzt, einen echten Ort unveraendert', () => {
    const t = (key: string) => `[${key}]`;
    expect(displayLocation('various', t)).toBe('[tournamentDirectory.variousVenues]');
    expect(displayLocation('Varnsdorf', t)).toBe('Varnsdorf');
    expect(displayLocation(null, t)).toBe('');
  });

  it('findet gleichnamige Turniere am selben Tag, nicht an verschiedenen Tagen', () => {
    const e = (id: string, name: string, startDate: string) => ({ id, name, startDate }) as DirectoryEntry;
    const ids = sameNameSiblings([
      e('1', 'O přeborníka 2. turnaj', '2026-10-11'),
      e('2', 'O přeborníka 2. turnaj ', '2026-10-11'),
      e('3', 'O přeborníka 2. turnaj', '2026-10-18'),
      e('4', 'Open Braunau', '2026-10-11'),
    ]);
    expect([...ids.keys()].sort()).toEqual(['1', '2']);
    // z2-t-cal-gleichnamig: durchgezählt in Listenreihenfolge.
    expect(ids.get('1')).toEqual({ index: 1, total: 2 });
    expect(ids.get('2')).toEqual({ index: 2, total: 2 });
  });

  it('z2-t-cal-gleichnamig: stellt gleichnamige Einträge desselben Tags direkt hintereinander, sonst stabil', () => {
    const e = (id: string, name: string, startDate: string) => ({ id, name, startDate }) as DirectoryEntry;
    const list = [
      e('a', 'Marburger Schachtage 2026 A Turnier', '2026-10-15'),
      e('b', 'Open Braunau', '2026-10-15'),
      e('c', 'Herbstopen', '2026-10-16'),
      e('d', 'Marburger Schachtage 2026 A Turnier ', '2026-10-15'),
      e('e', 'Open Braunau', '2026-10-22'),
    ];
    expect(groupSameName(list).map(x => x.id)).toEqual(['a', 'd', 'b', 'c', 'e']);
    expect(groupSameName([])).toEqual([]);
  });
});

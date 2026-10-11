import { DirectoryEntry, displayLocation, isPlaceholderLocation, sameNameSiblings } from './tournament-directory.model';

/** UI-Sweep 2026-10-11 (r2-t-various, r2-t-dup-cards): reine Helfer der Turnierkarten. */
describe('tournament-directory.model', () => {
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
    expect([...ids].sort()).toEqual(['1', '2']);
  });
});

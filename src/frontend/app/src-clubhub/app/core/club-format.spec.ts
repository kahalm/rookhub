import { STATUS_LABEL, ageClass, attendanceText, byInitial, dateNeighbours, emptyInput, firstPhone, formatBirth, formatLinkCode, fullName, isoDate, longDate, nameHead, nameTail, parseBirth, shortDate, sortName, telHref, trainingDate, trainsToday, weekdayName } from './club-format';

describe('club-format', () => {
  it('Blättern: die Einheit davor und danach — der Tag, für den die Liste aufgeht, zählt als letzter Halt mit', () => {
    const dates = ['2026-09-11', '2026-09-18', '2026-09-25'];
    // Auf dem heutigen Trainingstag (noch nichts erfasst): zurück zur letzten Einheit, weiter gibt es nichts.
    expect(dateNeighbours(dates, '2026-10-02', '2026-10-02')).toEqual({ prev: '2026-09-25', next: null });
    expect(dateNeighbours(dates, '2026-09-25', '2026-10-02')).toEqual({ prev: '2026-09-18', next: '2026-10-02' });
    expect(dateNeighbours(dates, '2026-09-11', '2026-10-02')).toEqual({ prev: null, next: '2026-09-18' });
    // Ein frei gewählter Tag ohne Einheit steht zwischen seinen Nachbarn.
    expect(dateNeighbours(dates, '2026-09-20', '2026-10-02')).toEqual({ prev: '2026-09-18', next: '2026-09-25' });
    // Reihenfolge und Doppelte der Eingabe sind egal; ohne jede Einheit gibt es nichts zu blättern.
    expect(dateNeighbours(['2026-09-25', '2026-09-11', '2026-09-25'], '2026-09-25', '2026-09-25')).toEqual({ prev: '2026-09-11', next: null });
    expect(dateNeighbours([], '2026-10-02', '2026-10-02')).toEqual({ prev: null, next: null });
    expect(dateNeighbours(['kaputt'], '2026-10-02', '')).toEqual({ prev: null, next: null });
  });

  it('ein leeres Blatt hat weder Nummern noch Gruppen', () => {
    expect(emptyInput()).toEqual({ firstName: '', lastName: '', birthDate: null, birthYear: null, level: null, fideId: null, nationalId: null,
      archived: false, isTrainer: false, contacts: [], groupIds: [] });
  });

  it('Altersklasse zählt nach Jahrgang: U10 spielt, wer heuer höchstens 10 wird', () => {
    expect(ageClass(2016, 2026)).toBe('U10');
    expect(ageClass(2015, 2026)).toBe('U12');
    expect(ageClass(2019, 2026)).toBe('U8');
    expect(ageClass(2008, 2026)).toBe('U18');
    expect(ageClass(2007, 2026)).toBeNull();      // 19 = erwachsen
    expect(ageClass(null, 2026)).toBeNull();
    expect(ageClass(2030, 2026)).toBeNull();
  });

  it('liest Geburtsdatum ODER Jahrgang, wie man es hier schreibt', () => {
    expect(parseBirth('')).toEqual({ birthDate: null, birthYear: null });
    expect(parseBirth(' 2015 ')).toEqual({ birthDate: null, birthYear: 2015 });
    expect(parseBirth('12.3.2015')).toEqual({ birthDate: '2015-03-12', birthYear: 2015 });
    expect(parseBirth('12.03.2015')).toEqual({ birthDate: '2015-03-12', birthYear: 2015 });
    expect(parseBirth('2015-03-12')).toEqual({ birthDate: '2015-03-12', birthYear: 2015 });
    expect(parseBirth('31.2.2015')).toBeNull();   // den Tag gibt es nicht
    expect(parseBirth('März 2015')).toBeNull();
    expect(parseBirth('15')).toBeNull();
  });

  it('zeigt das Datum so, wie es wieder gelesen wird', () => {
    expect(formatBirth({ birthDate: '2015-03-12', birthYear: 2015 })).toBe('12.03.2015');
    expect(formatBirth({ birthDate: null, birthYear: 2015 })).toBe('2015');
    expect(formatBirth({})).toBe('');
    expect(parseBirth(formatBirth({ birthDate: '2015-03-12' }))?.birthDate).toBe('2015-03-12');
  });

  it('baut aus einer Nummer, wie sie auf dem Zettel steht, einen wählbaren Link', () => {
    expect(telHref('0512/58 12 34')).toBe('tel:0512581234');
    expect(telHref(' +43 (660) 123-45 67 ')).toBe('tel:+436601234567');
    expect(firstPhone([{ kind: 'email', value: 'a@b.at' }, { kind: 'phone', value: '0660 1', label: 'Mutter' }])?.label).toBe('Mutter');
    expect(firstPhone([{ kind: 'email', value: 'a@b.at' }])).toBeNull();
  });

  it('Trainingstag: am Freitag heute, sonst der vergangene Freitag — nie ein künftiger', () => {
    const friday = new Date(2026, 8, 25, 18, 0);          // Fr 25.09.2026
    const monday = new Date(2026, 8, 28, 9, 0);
    const thursday = new Date(2026, 9, 1, 23, 30);
    expect(trainingDate(5, friday)).toBe('2026-09-25');
    expect(trainingDate(5, monday)).toBe('2026-09-25');
    expect(trainingDate(5, thursday)).toBe('2026-09-25');
    expect(trainingDate(null, monday)).toBe('2026-09-28');  // ohne festen Tag: heute
    expect(trainsToday(5, friday)).toBeTrue();
    expect(trainsToday(5, monday)).toBeFalse();
    expect(trainsToday(null, friday)).toBeFalse();
    expect(weekdayName(5)).toBe('Freitag');
    expect(weekdayName(null)).toBe('');
  });

  it('das Datum in Ortszeit, nicht in UTC', () => {
    expect(isoDate(new Date(2026, 8, 25, 0, 30))).toBe('2026-09-25');
    expect(isoDate(new Date(2026, 8, 25, 23, 30))).toBe('2026-09-25');
    expect(shortDate('2026-09-25')).toBe('Fr 25.09.');
    expect(longDate('2026-09-25')).toBe('Freitag, 25. September 2026');
    expect(longDate('2026-01-02')).toBe('Freitag, 2. Jänner 2026');
  });

  it('Einmal-Code in zwei Gruppen, Anwesenheit in Worten', () => {
    expect(formatLinkCode('ABCDEFGHJK')).toBe('ABCDE-FGHJK');
    expect(attendanceText(8, 10)).toBe('8 von 10 Einheiten da');
    expect(attendanceText(1, 1)).toBe('1 von 1 Einheit da');
    expect(attendanceText(0, 0)).toBe('');
  });

  it('Register wie im Karteikasten: nach Anfangsbuchstaben, Umlaut beim Grundbuchstaben', () => {
    const rows = [{ firstName: 'x', lastName: 'Auer' }, { firstName: 'x', lastName: 'Äpfelbacher' }, { firstName: 'x', lastName: 'Berger' },
      { firstName: 'x', lastName: 'Šarić' }, { firstName: '', lastName: '' }];
    expect(byInitial(rows).map(r => [r.letter, r.items.length])).toEqual([['A', 2], ['B', 1], ['S', 1], ['#', 1]]);
  });

  it('der Nachname ist optional: ohne ihn ordnet und zeigt die Kartei den Vornamen', () => {
    const emil = { firstName: 'Emil', lastName: '' };
    const anna = { firstName: 'Anna', lastName: 'Auer' };
    expect([sortName(emil), nameHead(emil), nameTail(emil), fullName(emil)]).toEqual(['Emil', 'Emil', '', 'Emil']);
    expect([sortName(anna), nameHead(anna), nameTail(anna), fullName(anna)]).toEqual(['Auer', 'Auer', ' Anna', 'Anna Auer']);
    expect(nameHead({ firstName: 'Emil', lastName: '  ' })).toBe('Emil');                // nur Leerzeichen = kein Nachname
    expect(byInitial([anna, emil]).map(r => r.letter)).toEqual(['A', 'E']);
  });

  it('Anwesenheit kennt zwei Zustände: da und gefehlt', () => {
    expect(STATUS_LABEL).toEqual({ present: 'da', absent: 'gefehlt' });
  });
});

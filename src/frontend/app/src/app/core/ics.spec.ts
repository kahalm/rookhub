import { buildIcs, icsFileName, downloadIcs } from './ics';

/**
 * Der Dateiinhalt ist ein Vertrag mit fremden Programmen (Google Kalender, Apple Kalender,
 * Outlook, Thunderbird) — er laesst sich nicht „ein bisschen falsch" schreiben. Geprueft wird
 * deshalb genau das, was diese Programme streng nehmen: das ausschliessliche Enddatum, die
 * Maskierung der Steuerzeichen, der Zeilenumbruch nach 75 Oktetten und CRLF.
 */
describe('ics', () => {
  const stamp = new Date('2026-09-07T08:30:00.000Z');

  function ics(over: Partial<Parameters<typeof buildIcs>[0]> = {}): string {
    return buildIcs({
      uid: 'tnr1@rookhub.example', title: 'Open Braunau', start: '2026-12-18',
      end: '2026-12-20', stamp, ...over,
    });
  }

  it('schreibt einen Ganztages-Termin mit AUSSCHLIESSLICHEM Enddatum', () => {
    // Ein Turnier vom 18. bis 20. endet fuer den Kalender am 21. — ohne diesen Tag fehlt in
    // jeder Kalender-App der letzte Turniertag.
    const text = ics();
    expect(text).toContain('DTSTART;VALUE=DATE:20261218');
    expect(text).toContain('DTEND;VALUE=DATE:20261221');
  });

  it('macht aus einem Turnier ohne Enddatum einen Ein-Tages-Termin', () => {
    const text = ics({ end: null });
    expect(text).toContain('DTSTART;VALUE=DATE:20261218');
    expect(text).toContain('DTEND;VALUE=DATE:20261219');
  });

  it('rechnet den Folgetag ueber Monats- und Jahresgrenzen', () => {
    expect(ics({ start: '2026-12-31', end: '2026-12-31' })).toContain('DTEND;VALUE=DATE:20270101');
    expect(ics({ start: '2028-02-28', end: '2028-02-28' })).toContain('DTEND;VALUE=DATE:20280229');
  });

  it('maskiert Semikolon, Komma, Backslash und Zeilenumbruch', () => {
    const text = ics({
      title: 'Open; A, B \\ C',
      location: 'Halle 1,\nEichetstrasse 29',
    });
    expect(text).toContain('SUMMARY:Open\; A\\, B \\\\ C');
    expect(text).toContain('Halle 1\\,\\nEichetstrasse 29');
  });

  it('bricht lange Zeilen nach 75 Oktetten um (Fortsetzung mit Leerzeichen)', () => {
    const text = ics({ title: 'A'.repeat(200) });
    for (const line of text.split('\r\n')) {
      expect(new TextEncoder().encode(line).length).withContext(line.slice(0, 40)).toBeLessThanOrEqual(75);
    }
    expect(text).toContain('\r\n ');          // es wurde wirklich umgebrochen
  });

  it('zerreisst dabei keine Mehrbyte-Zeichen', () => {
    // „ö" sind zwei Oktette: ein Umbruch mitten hindurch macht die Datei unlesbar.
    const text = ics({ title: 'ö'.repeat(120) });
    for (const line of text.split('\r\n')) {
      expect(line).not.toContain('�');
      expect(new TextEncoder().encode(line).length).toBeLessThanOrEqual(75);
    }
    // Alle 120 Zeichen sind noch da (ohne die Umbruch-Leerzeichen gezaehlt).
    const summary = text.split('SUMMARY:')[1].split('\r\nLOCATION')[0].split('\r\nURL')[0];
    expect(summary.replace(/\r\n /g, '').replace(/\r\n.*/s, '')).toBe('ö'.repeat(120));
  });

  it('benutzt CRLF und endet mit einer abgeschlossenen Zeile', () => {
    const text = ics();
    expect(text.endsWith('END:VCALENDAR\r\n')).toBeTrue();
    expect(text.includes('\n\n')).toBeFalse();
  });

  it('traegt Kennung, Zeitstempel und die freiwilligen Felder', () => {
    const text = ics({ description: 'Sieben Runden', url: 'https://example.test/tnr1' });
    expect(text).toContain('UID:tnr1@rookhub.example');
    expect(text).toContain('DTSTAMP:20260907T083000Z');
    expect(text).toContain('DESCRIPTION:Sieben Runden');
    expect(text).toContain('URL:https://example.test/tnr1');
  });

  it('laesst leere Felder ganz weg, statt sie leer zu schreiben', () => {
    const text = ics({ location: null, description: '', url: undefined });
    expect(text).not.toContain('LOCATION:');
    expect(text).not.toContain('DESCRIPTION:');
    expect(text).not.toContain('URL:');
  });

  describe('icsFileName', () => {
    it('macht aus dem Titel einen unverfaenglichen Dateinamen', () => {
      expect(icsFileName('5° Torneo "ad Gredine" – Op')).toBe('5-Torneo-ad-Gredine-Op.ics');
    });

    it('faellt auf einen Namen zurueck, wenn nichts Brauchbares uebrig bleibt', () => {
      expect(icsFileName('///')).toBe('termin.ics');
    });

    it('kuerzt sehr lange Titel', () => {
      expect(icsFileName('A'.repeat(200)).length).toBeLessThanOrEqual(64);
    });
  });

  describe('downloadIcs', () => {
    it('legt die Datei vor und raeumt das Hilfselement wieder weg', () => {
      const before = document.body.childElementCount;
      const clicks: string[] = [];
      // Den Klick abfangen: der Testlaeufer soll nichts herunterladen.
      const original = HTMLAnchorElement.prototype.click;
      HTMLAnchorElement.prototype.click = function (this: HTMLAnchorElement) { clicks.push(this.download); };
      try {
        expect(downloadIcs('BEGIN:VCALENDAR\r\nEND:VCALENDAR\r\n', 'test.ics')).toBeTrue();
      } finally {
        HTMLAnchorElement.prototype.click = original;
      }
      expect(clicks).toEqual(['test.ics']);
      expect(document.body.childElementCount).toBe(before);
    });
  });
});

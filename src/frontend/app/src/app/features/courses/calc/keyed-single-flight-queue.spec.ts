import { KeyedSingleFlightQueue, SingleFlight } from './keyed-single-flight-queue';

describe('KeyedSingleFlightQueue', () => {
  function make() {
    const sent: SingleFlight<number, string>[] = [];
    const q = new KeyedSingleFlightQueue<number, string>(f => sent.push(f));
    return { q, sent };
  }

  it('schickt in Einreihungs-Reihenfolge, je Schlüssel höchstens eine Anfrage', () => {
    const { q, sent } = make();
    q.set(2, 'a'); q.set(1, 'b');
    q.flush();
    expect(sent.map(f => [f.key, f.value])).toEqual([[2, 'a'], [1, 'b']]);
    q.set(2, 'a2');
    q.flush();
    expect(sent.length).withContext('2 ist noch unterwegs').toBe(2);
    expect(q.isInFlight(2)).toBeTrue();
    expect(q.size).toBe(1);
  });

  it('fasst gleiche Schlüssel zusammen: nur der jüngste wartende Stand geht nach der Antwort raus', () => {
    const { q, sent } = make();
    q.set(7, 'v1'); q.flush();
    q.set(7, 'v2'); q.set(7, 'v3');
    q.flush();
    expect(sent.length).toBe(1);
    q.succeeded(sent[0]);
    expect(sent.map(f => f.value)).toEqual(['v1', 'v3']);
    expect(sent[1].ticket).toBe(2);
  });

  it('Fehlschlag: reiht nach der Regel wieder ein, sendet aber nicht sofort neu', () => {
    const { q, sent } = make();
    q.set(7, 'v1'); q.flush();
    q.failed(sent[0], waiting => waiting === undefined ? sent[0].value : undefined);
    expect(sent.length).toBe(1);
    expect(q.get(7)).toBe('v1');
    expect(q.isInFlight(7)).toBeFalse();
    q.flush();
    expect(sent.length).toBe(2);
  });

  it('Fehlschlag mit wartendem jüngeren Stand: die Regel sieht ihn und darf zusammenführen', () => {
    const { q, sent } = make();
    q.set(7, 'alt'); q.flush();
    q.set(7, 'neu');
    q.failed(sent[0], waiting => `${sent[0].value}+${waiting}`);
    expect(q.get(7)).toBe('alt+neu');
    const keep = make();
    keep.q.set(7, 'alt'); keep.q.flush();
    keep.q.set(7, 'neu');
    keep.q.failed(keep.sent[0], () => undefined);
    expect(keep.q.get(7)).withContext('undefined = wartenden Stand lassen').toBe('neu');
  });

  it('ein überholter Versand reiht beim Fehlschlag nichts mehr ein', () => {
    const { q, sent } = make();
    q.set(7, 'v1'); q.flush();
    const stale = sent[0];
    q.succeeded(stale);
    q.set(7, 'v2'); q.flush();
    q.failed(stale, () => 'v1');
    expect(q.has(7)).toBeFalse();
  });

  it('delete verwirft nur den wartenden Stand', () => {
    const { q, sent } = make();
    q.set(7, 'v1'); q.flush();
    q.set(7, 'v2'); q.delete(7);
    q.succeeded(sent[0]);
    expect(sent.length).toBe(1);
  });

  it('verträgt synchrone Antworten (Antwort noch innerhalb von dispatch)', () => {
    const sent: string[] = [];
    let q!: KeyedSingleFlightQueue<number, string>;
    q = new KeyedSingleFlightQueue<number, string>(f => {
      sent.push(`${f.key}:${f.value}`);
      if (f.value === 'a1') q.set(1, 'a2');
      q.succeeded(f);
    });
    q.set(1, 'a1'); q.set(2, 'b1');
    q.flush();
    // Der nachgereihte Stand von 1 steht HINTER 2 (Einreihungs-Reihenfolge); nichts geht doppelt raus.
    expect(sent).toEqual(['1:a1', '2:b1', '1:a2']);
  });
});

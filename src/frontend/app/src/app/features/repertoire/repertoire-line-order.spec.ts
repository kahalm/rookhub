import {
  DEFAULT_LINE_ORDER, LINE_ORDERS, availableLineOrders, effectiveLineOrder, lineOrderHintKey, lineOrderLabelKey, parseLineOrder,
  readLegacyFrequencyOrder,
} from './repertoire-line-order';

describe('repertoire-line-order', () => {
  // Spiegel-Pin: dieselbe Liste steht im Backend als RepertoireLineOrders.All (Services/Repertoires/RepertoireLineOrders.cs;
  // dort festgenagelt in RepertoireTrainingServiceTests.LineOrders_MatchTheFrontendMirror). BEIDE Seiten prüfen LITERALE
  // Werte, nicht die jeweils andere Implementierung: kennt nur das Frontend einen Wert, scheitert das Speichern mit 400;
  // kennt ihn nur der Server, fällt das Lesen hier still auf die Vorgabe. Wer hier etwas ändert, ändert den Test dort mit.
  it('LINE_ORDERS = die sechs Werte des Servers, Vorgabe „Wenig trainierte zuerst“', () => {
    expect([...LINE_ORDERS]).toEqual(['leastTrained', 'mostTrained', 'random', 'repertoire', 'frequency', 'opponent']);
    expect(DEFAULT_LINE_ORDER).toBe('leastTrained');
  });

  it('availableLineOrders: „Nach dem Gegner“ steht nur mit gespeichertem Gegner zur Wahl (der Server lehnt es sonst ab)', () => {
    expect(availableLineOrders(false)).toEqual(['leastTrained', 'mostTrained', 'random', 'repertoire', 'frequency']);
    expect(availableLineOrders(true)).toEqual(['leastTrained', 'mostTrained', 'random', 'repertoire', 'frequency', 'opponent']);
  });

  it('parseLineOrder: nur Werte der Liste, alles andere null', () => {
    for (const o of LINE_ORDERS) expect(parseLineOrder(o)).toBe(o);
    for (const bad of [null, undefined, '', 'LeastTrained', ' random', 'alphabetical', 3, {}, ['random']]) {
      expect(parseLineOrder(bad)).withContext(JSON.stringify(bad)).toBeNull();
    }
  });

  describe('effectiveLineOrder', () => {
    it('ohne gespeicherten Wert: die Vorgabe', () => {
      expect(effectiveLineOrder(null, false)).toBe('leastTrained');
      expect(effectiveLineOrder(undefined, false)).toBe('leastTrained');
    });

    it('unbekannter gespeicherter Wert = nichts gewählt', () => {
      expect(effectiveLineOrder('alphabetical', false)).toBe('leastTrained');
      expect(effectiveLineOrder('alphabetical', true)).toBe('frequency');
    });

    it('alter Geräte-Schalter „Häufigste zuerst“ an und nichts gewählt: frequency gilt weiter', () => {
      expect(effectiveLineOrder(null, true)).toBe('frequency');
    });

    it('eine Wahl für das Repertoire schlägt den alten Schalter', () => {
      expect(effectiveLineOrder('repertoire', true)).toBe('repertoire');
      expect(effectiveLineOrder('leastTrained', true)).toBe('leastTrained');
      expect(effectiveLineOrder('mostTrained', false)).toBe('mostTrained');
    });
  });

  describe('effectiveLineOrder gegen einen gespeicherten Gegner (0.999.3)', () => {
    it('„Nach dem Gegner“ gilt nur mit gespeichertem Gegner — ohne ihn fällt es auf die Vorgabe', () => {
      expect(effectiveLineOrder('opponent', false, true)).toBe('opponent');
      expect(effectiveLineOrder('opponent', false, false)).toBe('leastTrained');
      expect(effectiveLineOrder('opponent', false)).toBe('leastTrained');
      // auch der alte Schalter macht daraus nicht „Häufigste zuerst“: gewählt war etwas anderes
      expect(effectiveLineOrder('opponent', true, false)).toBe('leastTrained');
    });

    it('nichts gewählt: die Vorgabe — der alte Schalter zählt dort nicht (er hatte in der Gegner-Sitzung nie Wirkung)', () => {
      expect(effectiveLineOrder(null, false, true)).toBe('leastTrained');
      expect(effectiveLineOrder(null, true, true)).toBe('leastTrained');
      expect(effectiveLineOrder(null, true, false)).toBe('frequency');   // ohne Gegner wie bisher
    });

    it('jede andere Wahl gilt mit und ohne Gegner', () => {
      for (const o of ['leastTrained', 'mostTrained', 'random', 'repertoire', 'frequency']) {
        expect(effectiveLineOrder(o, true, true)).toBe(o as never);
        expect(effectiveLineOrder(o, true, false)).toBe(o as never);
      }
    });
  });

  describe('readLegacyFrequencyOrder (Schalter bis 0.995.0)', () => {
    afterEach(() => localStorage.removeItem('rookhub_rep_train_freq_order'));

    it('nur „1“ heißt an', () => {
      expect(readLegacyFrequencyOrder()).toBeFalse();
      localStorage.setItem('rookhub_rep_train_freq_order', '0');
      expect(readLegacyFrequencyOrder()).toBeFalse();
      localStorage.setItem('rookhub_rep_train_freq_order', '1');
      expect(readLegacyFrequencyOrder()).toBeTrue();
    });
  });

  it('i18n-Schlüssel je Reihenfolge', () => {
    expect(lineOrderLabelKey('mostTrained')).toBe('repertoire.trainOrder.option.mostTrained');
    expect(lineOrderHintKey('frequency')).toBe('repertoire.trainOrder.hint.frequency');
    expect(lineOrderLabelKey('opponent')).toBe('repertoire.trainOrder.option.opponent');
  });
});

import {
  DEFAULT_LINE_ORDER, LINE_ORDERS, effectiveLineOrder, lineOrderHintKey, lineOrderLabelKey, parseLineOrder,
  readLegacyFrequencyOrder,
} from './repertoire-line-order';

describe('repertoire-line-order', () => {
  // Spiegel-Pin: dieselbe Liste steht im Backend als RepertoireLineOrders.All (Services/Repertoires/RepertoireLineOrders.cs;
  // dort festgenagelt in RepertoireTrainingServiceTests.LineOrders_MatchTheFrontendMirror). BEIDE Seiten prüfen LITERALE
  // Werte, nicht die jeweils andere Implementierung: kennt nur das Frontend einen Wert, scheitert das Speichern mit 400;
  // kennt ihn nur der Server, fällt das Lesen hier still auf die Vorgabe. Wer hier etwas ändert, ändert den Test dort mit.
  it('LINE_ORDERS = die fünf Werte des Servers, Vorgabe „Wenig trainierte zuerst“', () => {
    expect([...LINE_ORDERS]).toEqual(['leastTrained', 'mostTrained', 'random', 'repertoire', 'frequency']);
    expect(DEFAULT_LINE_ORDER).toBe('leastTrained');
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
  });
});

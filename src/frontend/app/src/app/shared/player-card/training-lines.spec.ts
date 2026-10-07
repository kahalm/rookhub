import { lineText, percent, trainingFilterParams, trainingLinesParams, trainingLinesUrl } from './training-lines';

describe('training-lines (Hilfen)', () => {
  it('percent: ganze Prozente ab 10, sonst eine Stelle mit Komma, winzig als „<0,1 %"', () => {
    expect(percent(0.5)).toBe('50 %');
    expect(percent(0.035)).toBe('3,5 %');
    expect(percent(0.0004)).toBe('<0,1 %');
    expect(percent(0)).toBe('0 %');
  });

  it('lineText: deutsche Notation mit Zugnummern, auch ab einer Stellung mit Schwarz am Zug', () => {
    expect(lineText(['e4', 'c5', 'Nf3', 'Nc6', 'Bb5', 'Qb6', 'O-O'], null)).toBe('1.e4 c5 2.Sf3 Sc6 3.Lb5 Db6 4.O-O');
    expect(lineText(['e5', 'Nf3', 'Nc6'], 'rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 1')).toBe('1…e5 2.Sf3 Sc6');
  });

  it('trainingLinesUrl: nur prep:<Zahl> oder league:<FIDE>', () => {
    expect(trainingLinesUrl('prep:42')).toBe('/api/prep/player/42/training-lines');
    expect(trainingLinesUrl('league:1606921')).toBe('/api/league/player/1606921/training-lines');
    expect(trainingLinesUrl('league:../x')).toBeNull();
    expect(trainingLinesUrl('evil:1')).toBeNull();
    expect(trainingLinesUrl(null)).toBeNull();
  });

  it('Filter: source steht immer dabei (ohne nimmt der Server Brett + online); Tempo/unsicher nur mit online', () => {
    expect(trainingFilterParams({ source: 'board', speeds: ['blitz'], years: null, withUnsure: true })).toEqual({ source: 'board' });
    expect(trainingFilterParams({ source: 'both', speeds: ['blitz', 'rapid'], years: 5, withUnsure: true }))
      .toEqual({ source: 'both', speeds: 'blitz,rapid', unsure: 'true', years: '5' });
    expect(trainingFilterParams(null)).toEqual({});
  });

  it('Parameter der Anfrage', () => {
    const p = trainingLinesParams({ repertoire: 7, color: 'b', chapterColors: { Kapitel: 'w' }, take: 5000,
      filter: { source: 'online', speeds: [], years: null, withUnsure: false } });
    expect(p.get('repertoire')).toBe('7');
    expect(p.get('color')).toBe('b');
    expect(JSON.parse(p.get('chapterColors')!)).toEqual({ Kapitel: 'w' });
    expect(p.get('take')).toBe('5000');
    expect(p.get('source')).toBe('online');
    const empty = trainingLinesParams({ repertoire: null, color: null, chapterColors: {} });
    expect(empty.keys()).toEqual([]);
  });
});

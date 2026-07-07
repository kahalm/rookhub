import { clearLastSolved, loadLastSolved, saveLastSolved, LastSolvedInfo } from './last-solved-store';

describe('last-solved-store', () => {
  beforeEach(() => sessionStorage.clear());

  const info: LastSolvedInfo = { id: 42, fen: '8/8/8/8/8/8/8/8 w - - 0 1', moves: 'e2e4 e7e5', orientation: 'white' };

  it('round-trips info through sessionStorage', () => {
    saveLastSolved('book', info);
    expect(loadLastSolved('book')).toEqual(info);
  });

  it('keeps sources isolated', () => {
    saveLastSolved('book', info);
    expect(loadLastSolved('standard')).toBeNull();
    expect(loadLastSolved('endless')).toBeNull();
  });

  it('returns null when nothing was saved', () => {
    expect(loadLastSolved('book')).toBeNull();
  });

  it('returns null and does not throw on corrupt entry', () => {
    sessionStorage.setItem('rookhub_last_solved_book', '{not-json');
    expect(loadLastSolved('book')).toBeNull();
  });

  it('rejects entries with invalid shape', () => {
    sessionStorage.setItem('rookhub_last_solved_book', JSON.stringify({ id: 'x', fen: 1, moves: null, orientation: 'up' }));
    expect(loadLastSolved('book')).toBeNull();
  });

  it('clears the stored entry', () => {
    saveLastSolved('book', info);
    clearLastSolved('book');
    expect(loadLastSolved('book')).toBeNull();
  });
});

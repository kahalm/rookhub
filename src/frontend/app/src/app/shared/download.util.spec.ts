import { downloadBlob } from './download.util';

describe('downloadBlob', () => {
  it('creates an anchor with the filename, clicks it, and revokes the object URL', () => {
    const click = jasmine.createSpy('click');
    const anchor = { href: '', download: '', click } as unknown as HTMLAnchorElement;
    const createEl = spyOn(document, 'createElement').and.returnValue(anchor);
    const createUrl = spyOn(URL, 'createObjectURL').and.returnValue('blob:abc');
    const revokeUrl = spyOn(URL, 'revokeObjectURL');

    const blob = new Blob(['x'], { type: 'text/plain' });
    downloadBlob(blob, 'game.pgn');

    expect(createEl).toHaveBeenCalledWith('a');
    expect(createUrl).toHaveBeenCalledWith(blob);
    expect(anchor.download).toBe('game.pgn');
    expect(anchor.href).toBe('blob:abc');
    expect(click).toHaveBeenCalled();
    expect(revokeUrl).toHaveBeenCalledWith('blob:abc');
  });
});

import { WeeklyListComponent } from './weekly-list.component';

describe('WeeklyListComponent file validation', () => {
  let component: WeeklyListComponent;
  let infoCalls: number;

  beforeEach(() => {
    infoCalls = 0;
    const snackbar = { info: () => { infoCalls++; } } as any;
    const translate = { instant: (k: string) => k } as any;
    component = new WeeklyListComponent({} as any, {} as any, snackbar, translate);
  });

  function selectFile(name: string, size: number): HTMLInputElement {
    const file = new File(['x'], name, { type: 'application/octet-stream' });
    Object.defineProperty(file, 'size', { value: size });
    const input = { files: [file], value: 'preset' } as unknown as HTMLInputElement;
    component.onFileSelected({ target: input } as unknown as Event);
    return input;
  }

  it('accepts a valid .pgn file', () => {
    selectFile('lines.pgn', 1024);
    expect(component.uploadFile).toBeTruthy();
    expect(component.uploadFileName).toBe('lines.pgn');
    expect(infoCalls).toBe(0);
  });

  it('rejects a non-.pgn file and clears the selection', () => {
    const input = selectFile('evil.exe', 1024);
    expect(component.uploadFile).toBeNull();
    expect(component.uploadFileName).toBe('');
    expect(input.value).toBe('');
    expect(infoCalls).toBe(1);
  });

  it('rejects a .pgn file larger than 10 MB', () => {
    selectFile('huge.pgn', 10 * 1024 * 1024 + 1);
    expect(component.uploadFile).toBeNull();
    expect(infoCalls).toBe(1);
  });
});

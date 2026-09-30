import { of } from 'rxjs';
import { WeeklyListComponent } from './weekly-list.component';

describe('WeeklyListComponent file validation', () => {
  let component: WeeklyListComponent;
  let infoCalls: number;

  beforeEach(() => {
    infoCalls = 0;
    const snackbar = { info: () => { infoCalls++; } } as any;
    const translate = { instant: (k: string) => k } as any;
    component = new WeeklyListComponent({} as any, {} as any, snackbar, translate, {} as any);
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

// F5-009: Termin wird als Wandzeit eingegeben, der Server vergleicht mit UtcNow -> als UTC mit Z schicken
// und den (zonenlosen) UTC-Wert des Servers in Ortszeit zurücklesen. Erwartungen über new Date(y, m, d, h, mi)
// -> gilt in jeder Zeitzone.
describe('WeeklyListComponent Termin als UTC', () => {
  let component: WeeklyListComponent;
  let weekly: any;

  beforeEach(() => {
    weekly = {
      create: jasmine.createSpy('create').and.returnValue(of({})),
      update: jasmine.createSpy('update').and.callFake((id: number, dto: any) => of({ id, scheduledAt: dto.scheduledAt })),
      getAll: jasmine.createSpy('getAll').and.returnValue(of([])),
    };
    const snackbar = { info: () => {} } as any;
    const translate = { instant: (k: string) => k } as any;
    component = new WeeklyListComponent({ isLoggedIn: false } as any, weekly, snackbar, translate, {} as any);
  });

  it('upload schickt die eingegebene Wandzeit als UTC-ISO mit Z', () => {
    component.uploadFile = new File(['1. e4 *'], 'post.pgn');
    component.uploadDate = '2026-06-08';
    component.uploadTime = '19:00';
    component.upload();
    expect(weekly.create).toHaveBeenCalled();
    expect(weekly.create.calls.mostRecent().args[1]).toBe(new Date(2026, 5, 8, 19, 0).toISOString());
  });

  it('Inline-Bearbeitung schickt die Wandzeit als UTC-ISO mit Z', () => {
    const row = { id: 7, title: 'T', description: null, scheduledAt: '', editDate: '2026-11-04', editTime: '19:00' } as any;
    component.savePost(row);
    expect(weekly.update.calls.mostRecent().args[1].scheduledAt).toBe(new Date(2026, 10, 4, 19, 0).toISOString());
  });

  it('liest den zonenlosen UTC-Termin des Servers in Ortszeit (Formular, Anzeige, Vorschlag)', () => {
    const at = new Date(2026, 5, 8, 19, 0);                        // eingegeben als 19:00 Ortszeit
    const fromApi = at.toISOString().replace(/\.\d{3}Z$/, '');     // DB-Wert ohne Zone, wie die API ihn liefert
    weekly.getAll.and.returnValue(of([{ id: 1, title: 'T', scheduledAt: fromApi }]));
    component.loadPosts();
    expect(component.rows[0].editDate).toBe('2026-06-08');
    expect(component.rows[0].editTime).toBe('19:00');
    expect(component.displayTime(component.rows[0])).toBe(at.getTime());
    expect(component.uploadDate).toBe('2026-06-15');
    expect(component.uploadTime).toBe('19:00');
  });
});

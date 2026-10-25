import { Subject } from 'rxjs';
import { DirectoryCalendarLoader, DirectoryListLoader, DirectoryPinsLoader } from './directory-loaders';
import {
  DirectoryCalendarDay, DirectoryEntry, DirectoryMapResponse, DirectoryPage, EMPTY_FILTER,
} from './tournament-directory.model';

const e = (id: string) => ({ id } as DirectoryEntry);
const filter = { ...EMPTY_FILTER };

/** Jede Anfrage bekommt ihren eigenen Strom — so lassen sich Antworten in beliebiger Reihenfolge schicken. */
function requests<T>() {
  const calls: { args: unknown[]; answer: Subject<T> }[] = [];
  const fn = (...args: unknown[]) => { const answer = new Subject<T>(); calls.push({ args, answer }); return answer; };
  return { calls, fn };
}

describe('DirectoryListLoader', () => {
  let r: ReturnType<typeof requests<DirectoryPage>>;
  let warned: number;
  let list: DirectoryListLoader;

  beforeEach(() => {
    r = requests<DirectoryPage>();
    warned = 0;
    list = new DirectoryListLoader((f, page, size) => r.fn(f, page, size), 25, () => warned++);
  });

  it('ersetzt mit Seite 1 und hängt weitere Seiten an', () => {
    list.load(filter);
    expect(r.calls[0].args).toEqual([filter, 1, 25]);
    expect(list.loading()).toBeTrue();
    r.calls[0].answer.next({ items: [e('a')], total: 3, truncated: true });
    expect([list.entries().length, list.total(), list.truncated(), list.loading()]).toEqual([1, 3, true, false]);

    list.page = 2;
    list.load(filter);
    expect(r.calls[1].args[1]).toBe(2);
    r.calls[1].answer.next({ items: [e('b')], total: 3, truncated: false });
    expect(list.entries().map(x => x.id)).toEqual(['a', 'b']);
  });

  it('verwirft die Antwort eines überholten Aufrufs', () => {
    list.page = 2;
    list.load(filter);
    list.page = 1;
    list.load(filter);
    r.calls[1].answer.next({ items: [e('neu')], total: 1, truncated: false });
    r.calls[0].answer.next({ items: [e('alt')], total: 9, truncated: false });
    r.calls[0].answer.error('spät');

    expect(list.entries().map(x => x.id)).toEqual(['neu']);
    expect(list.total()).toBe(1);
    expect(list.failed()).toBeFalse();
  });

  it('zeigt bei gescheiterter Seite 1 die Fehlerkarte statt der alten Liste', () => {
    list.load(filter);
    r.calls[0].answer.next({ items: [e('a')], total: 1, truncated: true });
    list.load(filter);
    r.calls[1].answer.error('500');

    expect([list.entries(), list.total(), list.truncated(), list.failed(), list.loading()])
      .toEqual([[], 0, false, true, false]);
    expect(warned).toBe(0);
  });

  it('behält bei gescheiterter Folgeseite die Liste, setzt die Seite zurück und meldet es', () => {
    list.load(filter);
    r.calls[0].answer.next({ items: [e('a')], total: 2, truncated: false });
    list.page = 2;
    list.load(filter);
    r.calls[1].answer.error('500');

    expect(list.entries().map(x => x.id)).toEqual(['a']);
    expect(list.page).toBe(1);
    expect(list.failed()).toBeFalse();
    expect(warned).toBe(1);
  });
});

describe('DirectoryPinsLoader', () => {
  it('lässt nur die jüngste Antwort gelten und räumt bei einem Fehler ab', () => {
    const r = requests<DirectoryMapResponse>();
    const pins = new DirectoryPinsLoader((f, b) => r.fn(f, b));

    pins.load(filter, 'alt');
    pins.load(filter, 'neu');
    expect(r.calls.map(c => c.args[1])).toEqual(['alt', 'neu']);
    r.calls[1].answer.next({ items: [e('n')], truncated: true });
    r.calls[0].answer.next({ items: [e('a')], truncated: false });
    expect([pins.pins().map(x => x.id), pins.truncated(), pins.loading()]).toEqual([['n'], true, false]);

    pins.load(filter, 'kaputt');
    r.calls[2].answer.error('500');
    expect([pins.pins(), pins.truncated(), pins.failed(), pins.loading()]).toEqual([[], false, true, false]);
  });
});

describe('DirectoryCalendarLoader', () => {
  it('lässt nur die Antwort des zuletzt gewählten Monats gelten', () => {
    const r = requests<DirectoryCalendarDay[]>();
    const cal = new DirectoryCalendarLoader((f, y, m) => r.fn(f, y, m));
    const day = (date: string) => ({ date } as unknown as DirectoryCalendarDay);

    cal.load(filter, 2026, 11);
    cal.load(filter, 2026, 12);
    r.calls[1].answer.next([day('2026-12-01')]);
    r.calls[0].answer.next([day('2026-11-01')]);
    expect(cal.days()).toEqual([day('2026-12-01')]);
    expect([cal.loading(), cal.failed()]).toEqual([false, false]);

    cal.load(filter, 2027, 1);
    r.calls[2].answer.error('500');
    expect([cal.days(), cal.failed(), cal.loading()]).toEqual([[], true, false]);
  });
});

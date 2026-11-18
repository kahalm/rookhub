import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { provideRouter, Router } from '@angular/router';
import { provideTranslateService, TranslateService } from '@ngx-translate/core';
import { of, Subject, throwError } from 'rxjs';
import { SnackbarService } from '../../core/snackbar.service';
import { WoodpeckerTileComponent } from './woodpecker-tile.component';
import { activeWoodpeckerPlan, WoodpeckerCycle, WoodpeckerPlan, WoodpeckerService, WoodpeckerStatus } from '../woodpecker/woodpecker.service';

const DAY = 86_400_000;
const iso = (offsetMs: number) => new Date(Date.now() + offsetMs).toISOString();

function cycle(number: number, over: Partial<WoodpeckerCycle> = {}): WoodpeckerCycle {
  return { number, targetDays: 28, startedAt: iso(-3 * DAY), deadline: iso(25 * DAY + 3_600_000), finishedAt: null,
    attempted: 0, solved: 0, timeSeconds: 0, ...over };
}

function plan(status: WoodpeckerStatus, cycles: WoodpeckerCycle[], over: Partial<WoodpeckerPlan> = {}): WoodpeckerPlan {
  return { id: 5, name: 'Taktik 1', createdAt: iso(-10 * DAY), completedAt: null, books: [], setSize: 12,
    availableCount: 200, currentCycle: cycles[cycles.length - 1].number, maxCycles: 7, status, nextCycleAt: null,
    cycles, ...over };
}

function render(p: WoodpeckerPlan | null, service: Partial<WoodpeckerService> = {}) {
  const snackbar = jasmine.createSpyObj<SnackbarService>('SnackbarService', ['warn']);
  TestBed.configureTestingModule({
    imports: [WoodpeckerTileComponent],
    providers: [provideRouter([]), provideTranslateService({ fallbackLang: 'en' }),
      { provide: WoodpeckerService, useValue: service }, { provide: SnackbarService, useValue: snackbar }],
  });
  const t = TestBed.inject(TranslateService);
  t.setTranslation('en', {
    dashboard: { woodpecker: { none: 'No training in progress.' } },
    woodpecker: {
      new: 'New training', continue: 'Continue', toOverview: 'To the overview', overdue: 'Deadline passed',
      daysLeft: '{{days}} days left', daysLeftOne: '{{days}} day left',
      setGrowing: 'Set: {{n}} puzzles of {{total}}', setGrowingOne: 'Set: {{n}} puzzle of {{total}}',
      cycleProgress: '{{done}} of {{total}} done',
      breakUntil: 'Break – cycle {{n}} can start from {{at}}.',
      ready: 'Cycle {{n}} is ready: {{size}} puzzles in {{days}} days.',
      readyOne: 'Cycle {{n}} is ready: {{size}} puzzles in {{days}} day.',
      startCycle: 'Start cycle {{n}}', saveFailed: 'Saving failed.',
      overview: { todayLabel: 'Today' }, daily: { count: '{{today}} of {{target}}', reached: 'Daily goal reached' },
    },
    apiErrors: { woodpecker_archived: 'This training is archived.' },
  });
  t.use('en');
  const fixture = TestBed.createComponent(WoodpeckerTileComponent);
  fixture.componentRef.setInput('plan', p);
  fixture.detectChanges();
  const el = fixture.nativeElement as HTMLElement;
  const actions = Array.from(el.querySelectorAll('mat-card-actions a')) as HTMLAnchorElement[];
  return { fixture, el, actions, snackbar, text: el.textContent!.replace(/\s+/g, ' ') };
}

describe('WoodpeckerTileComponent (Dashboard-Kachel)', () => {
  // 0.993.0, Wunsch 18.11.: „auch ohne Statistik visualisieren, wenn ich heute das notwendige Minimum erledigt habe
  // (sowohl im Dashboard …)“ — Zahlen: Satz 388, Frist übermorgen Mittag = 3 Kalendertage inkl. heute.
  describe('Tagesziel', () => {
    const noonIn = (n: number) => { const d = new Date(); return new Date(d.getFullYear(), d.getMonth(), d.getDate() + n, 12, 0, 0).toISOString(); };
    const running = (attempted: number, todayAttempted: number | undefined, over: Partial<WoodpeckerPlan> = {}) =>
      plan('solving', [cycle(1, { finishedAt: iso(-4 * DAY) }), cycle(2, { targetDays: 14, attempted, deadline: noonIn(2) })],
        { setSize: 388, todayAttempted, ...over });
    const goalText = (el: HTMLElement) => Array.from(el.querySelectorAll('.wp-daily .wdg > *')).map(x => x.textContent!.trim());

    it('noch offen: leise Zeile „Heute: 23 von 90“ unter der Frist, ohne Haken', () => {
      // 141 bearbeitet → 247 offen; Soll = ⌈(247 + 23) / 3⌉ = 90
      const { el } = render(running(141, 23));
      expect(goalText(el)).toEqual(['Today:', '23 of 90']);
      expect(el.querySelector('.wp-daily mat-icon')).toBeNull();
      const lines = Array.from(el.querySelectorAll('.wp-content > *')).map(x => x.className.split(' ')[0]);
      expect(lines.indexOf('wp-daily')).toBe(lines.indexOf('wp-deadline') + 1);
    });

    it('genau erreicht: grüner Haken + „Tagesziel erreicht“', () => {
      // 208 bearbeitet → 180 offen; Soll = ⌈(180 + 90) / 3⌉ = 90
      const { el } = render(running(208, 90));
      expect(goalText(el)).toEqual(['check_circle', 'Daily goal reached', '90 of 90']);
      expect(el.querySelector('.wp-daily .wdg-done mat-icon.wdg-check')).not.toBeNull();
    });

    it('übererfüllt: bleibt erreicht', () => {
      const { el } = render(running(218, 100));   // 170 offen; Soll = ⌈270 / 3⌉ = 90
      expect(goalText(el)).toEqual(['check_circle', 'Daily goal reached', '100 of 90']);
    });

    it('älterer Server ohne todayAttempted: keine Zeile (kein falsches „0 von n“)', () => {
      const { el } = render(running(141, undefined));
      expect(el.querySelector('.wp-daily')).toBeNull();
      expect(el.querySelector('app-woodpecker-daily-goal')).toBeNull();
    });

    it('Zyklus 1 hat kein Soll: nur „Heute: 5“', () => {
      const { el } = render(plan('solving', [cycle(1, { attempted: 12 })], { todayAttempted: 5 }));
      expect(goalText(el)).toEqual(['Today:', '5']);
      expect(el.querySelector('.wp-daily mat-icon')).toBeNull();
    });

    it('Frist vorbei, Pause und bereit: kein Tagesziel', () => {
      const late = render(plan('solving', [cycle(1, { finishedAt: iso(-9 * DAY) }), cycle(2, { attempted: 3, deadline: iso(-DAY) })], { todayAttempted: 3 }));
      expect(late.el.querySelector('.wp-daily')).toBeNull();
      expect(late.text).toContain('Deadline passed');
      TestBed.resetTestingModule();
      const pause = render(plan('break', [cycle(1, { finishedAt: iso(-3_600_000) })], { nextCycleAt: iso(DAY), todayAttempted: 40 }));
      expect(pause.el.querySelector('.wp-daily')).toBeNull();
      TestBed.resetTestingModule();
      const ready = render(plan('ready', [cycle(1, { finishedAt: iso(-2 * DAY) })], { todayAttempted: 0 }));
      expect(ready.el.querySelector('.wp-daily')).toBeNull();
    });

    it('neuer Planstand (z. B. nach dem Lösen zurück im Dashboard): die Zeile wechselt auf erreicht', () => {
      const { fixture, el } = render(running(207, 89));   // 181 offen; Soll = ⌈270 / 3⌉ = 90
      expect(goalText(el)).toEqual(['Today:', '89 of 90']);
      fixture.componentRef.setInput('plan', running(208, 90));
      fixture.detectChanges();
      expect(goalText(el)).toEqual(['check_circle', 'Daily goal reached', '90 of 90']);
    });
  });

  it('solving, Zyklus 1: Name, Satzgröße, Resttage, genau eine Hauptaktion „Weiter lösen“ in den Kurs-Löser', () => {
    const { el, actions, text } = render(plan('solving', [cycle(1)]));
    expect(el.querySelector('.wp-name')!.textContent!.trim()).toBe('Taktik 1');
    expect(text).toContain('Set: 12 puzzles of 200');
    expect(text).toContain('26 days left');
    expect(el.querySelector('mat-progress-bar')).toBeNull();          // Zyklus 1: kein Satz-Balken
    expect(actions.length).toBe(1);
    expect(actions[0].classList).toContain('wp-primary');
    expect(actions[0].textContent).toContain('Continue');
    expect(actions[0].getAttribute('href')).toBe('/woodpecker/5/solve');
  });

  it('solving, Zyklus 2: erledigt/Satz mit beschriftetem Balken', () => {
    const c2 = cycle(2, { targetDays: 14, attempted: 3, deadline: iso(5 * DAY - 3_600_000) });
    const { el, text } = render(plan('solving', [cycle(1, { finishedAt: iso(-4 * DAY) }), c2]));
    expect(text).toContain('3 of 12 done');
    const bar = el.querySelector('mat-progress-bar') as HTMLElement;
    expect(bar).not.toBeNull();
    expect(bar.getAttribute('aria-label')).toBe('3 of 12 done');
    expect(bar.getAttribute('aria-valuenow')).toBe('25');
    expect(text).toContain('5 days left');
  });

  it('solving mit überzogener Frist: „Frist überschritten“ in Warnfarbe', () => {
    const { el } = render(plan('solving', [cycle(1, { deadline: iso(-DAY) })]));
    const line = el.querySelector('.wp-deadline') as HTMLElement;
    expect(line.textContent!.trim()).toBe('Deadline passed');
    expect(line.classList).toContain('over');
  });

  it('break: Pause-Satz und nur ein Textlink zur Übersicht', () => {
    const p = plan('break', [cycle(1, { finishedAt: iso(-3_600_000) })], { nextCycleAt: iso(20 * 3_600_000) });
    const { actions, text } = render(p);
    expect(text).toContain('Break – cycle 2 can start from');
    expect(actions.length).toBe(1);
    expect(actions[0].classList).toContain('wp-overview');
    expect(actions[0].classList).not.toContain('wp-primary');
    expect(actions[0].getAttribute('href')).toBe('/woodpecker');
  });

  it('ready: Bereit-Satz mit halbierter Frist, Hauptknopf „Zyklus 2 starten“ (kein Link mehr zur Übersicht)', () => {
    const { el, actions, text } = render(plan('ready', [cycle(1, { finishedAt: iso(-2 * DAY) })]));
    expect(text).toContain('Cycle 2 is ready: 12 puzzles in 14 days.');
    expect(actions.length).toBe(0);
    const btn = el.querySelector('mat-card-actions button.wp-primary') as HTMLButtonElement;
    expect(btn.textContent!.replace(/\s+/g, ' ').trim()).toBe('replay Start cycle 2');
    expect(btn.disabled).toBeFalse();
  });

  it('ready: Klick startet den Zyklus direkt, sperrt mit Spinner, meldet den Plan und springt in den Löser', async () => {
    const ready = plan('ready', [cycle(1, { finishedAt: iso(-2 * DAY) })]);
    const started = plan('solving', [cycle(1, { finishedAt: iso(-2 * DAY) }), cycle(2, { targetDays: 14 })]);
    const answer = new Subject<WoodpeckerPlan>();
    const startNextCycle = jasmine.createSpy('startNextCycle').and.returnValue(answer);
    const { fixture, el, snackbar } = render(ready, { startNextCycle });
    const navigate = spyOn(TestBed.inject(Router), 'navigate').and.resolveTo(true);
    const emitted: (WoodpeckerPlan | null)[] = [];
    fixture.componentInstance.planChange.subscribe(p => emitted.push(p));

    (el.querySelector('button.wp-primary') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(startNextCycle).toHaveBeenCalledOnceWith(5);
    const btn = el.querySelector('button.wp-primary') as HTMLButtonElement;
    expect(btn.disabled).toBeTrue();
    expect(btn.querySelector('mat-spinner')).not.toBeNull();

    answer.next(started);
    answer.complete();
    expect(emitted).toEqual([started]);
    expect(navigate).toHaveBeenCalledOnceWith(['/woodpecker', 5, 'solve']);
    expect(snackbar.warn).not.toHaveBeenCalled();
  });

  it('ready: Fehler → Snackbar mit dem API-Grund, kein Sprung, Plan neu geladen und gemeldet', () => {
    const ready = plan('ready', [cycle(1, { finishedAt: iso(-2 * DAY) })]);
    const fresh = plan('break', [cycle(1, { finishedAt: iso(-3_600_000) })], { nextCycleAt: iso(3_600_000) });
    const err = new HttpErrorResponse({ status: 409, error: { message: 'archived', code: 'woodpecker_archived' } });
    const service = {
      startNextCycle: jasmine.createSpy('startNextCycle').and.returnValue(throwError(() => err)),
      list: jasmine.createSpy('list').and.returnValue(of([fresh])),
    };
    const { fixture, el, snackbar } = render(ready, service);
    const navigate = spyOn(TestBed.inject(Router), 'navigate');
    const emitted: (WoodpeckerPlan | null)[] = [];
    fixture.componentInstance.planChange.subscribe(p => emitted.push(p));

    (el.querySelector('button.wp-primary') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(snackbar.warn).toHaveBeenCalledOnceWith('This training is archived.');
    expect(navigate).not.toHaveBeenCalled();
    expect(service.list).toHaveBeenCalledTimes(1);
    expect(emitted).toEqual([fresh]);
    expect((el.querySelector('button.wp-primary') as HTMLButtonElement).disabled).toBeFalse();
  });

  it('kein laufendes Training: ein Satz und „Neues Training“ zur Übersicht', () => {
    const { el, actions, text } = render(null);
    expect(el.querySelector('.wp-name')).toBeNull();
    expect(text).toContain('No training in progress.');
    expect(actions.length).toBe(1);
    expect(actions[0].textContent!.trim()).toBe('New training');
    expect(actions[0].getAttribute('href')).toBe('/woodpecker');
  });
});

describe('activeWoodpeckerPlan', () => {
  it('überspringt abgeschlossene und nimmt das zuletzt bewegte Training', () => {
    const old = plan('solving', [cycle(1, { startedAt: iso(-20 * DAY) })], { id: 1, createdAt: iso(-20 * DAY) });
    // Älter angelegt, aber gestern einen Zyklus beendet → zuletzt bewegt.
    const moved = plan('break', [cycle(1, { startedAt: iso(-40 * DAY), finishedAt: iso(-DAY) })], { id: 2, createdAt: iso(-40 * DAY) });
    const done = plan('completed', [cycle(7, { startedAt: iso(-3_600_000), finishedAt: iso(-60_000) })], { id: 3 });
    expect(activeWoodpeckerPlan([done, old, moved])?.id).toBe(2);
  });

  it('Gleichstand: das erste der Liste (API: neueste zuerst); nur abgeschlossene → null', () => {
    const at = iso(-DAY);
    const a = plan('solving', [cycle(1, { startedAt: at })], { id: 1, createdAt: at });
    const b = plan('solving', [cycle(1, { startedAt: at })], { id: 2, createdAt: at });
    expect(activeWoodpeckerPlan([a, b])?.id).toBe(1);
    expect(activeWoodpeckerPlan([plan('completed', [cycle(7)])])).toBeNull();
    expect(activeWoodpeckerPlan([])).toBeNull();
  });

  it('archivierte zählen nie als laufend — auch nicht mitten im Zyklus und auch nicht nur über archivedAt (0.958.0)', () => {
    const recent = iso(-60_000);
    const archived = plan('archived', [cycle(1, { startedAt: recent })], { id: 7, createdAt: recent, archivedAt: recent });
    const legacy = plan('solving', [cycle(1, { startedAt: recent })], { id: 8, createdAt: recent, archivedAt: recent });
    const old = plan('solving', [cycle(1, { startedAt: iso(-20 * DAY) })], { id: 1, createdAt: iso(-20 * DAY) });
    expect(activeWoodpeckerPlan([archived, legacy, old])?.id).toBe(1);
    expect(activeWoodpeckerPlan([archived])).toBeNull();
  });
});

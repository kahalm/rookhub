import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideTranslateService, TranslateService } from '@ngx-translate/core';
import { WoodpeckerTileComponent } from './woodpecker-tile.component';
import { activeWoodpeckerPlan, WoodpeckerCycle, WoodpeckerPlan, WoodpeckerStatus } from '../woodpecker/woodpecker.service';

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

function render(p: WoodpeckerPlan | null) {
  TestBed.configureTestingModule({
    imports: [WoodpeckerTileComponent],
    providers: [provideRouter([]), provideTranslateService({ fallbackLang: 'en' })],
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
    },
  });
  t.use('en');
  const fixture = TestBed.createComponent(WoodpeckerTileComponent);
  fixture.componentRef.setInput('plan', p);
  fixture.detectChanges();
  const el = fixture.nativeElement as HTMLElement;
  const actions = Array.from(el.querySelectorAll('mat-card-actions a')) as HTMLAnchorElement[];
  return { el, actions, text: el.textContent!.replace(/\s+/g, ' ') };
}

describe('WoodpeckerTileComponent (Dashboard-Kachel)', () => {
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

  it('ready: Bereit-Satz mit halbierter Frist, Hauptaktion führt zur Übersicht (dort startet der Zyklus)', () => {
    const { actions, text } = render(plan('ready', [cycle(1, { finishedAt: iso(-2 * DAY) })]));
    expect(text).toContain('Cycle 2 is ready: 12 puzzles in 14 days.');
    expect(actions.length).toBe(1);
    expect(actions[0].classList).toContain('wp-primary');
    expect(actions[0].getAttribute('href')).toBe('/woodpecker');
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
});

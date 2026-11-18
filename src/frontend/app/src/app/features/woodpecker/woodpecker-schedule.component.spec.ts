import { TestBed } from '@angular/core/testing';
import { provideTranslateService, TranslateService } from '@ngx-translate/core';
import { WoodpeckerScheduleComponent } from './woodpecker-schedule.component';
import { WoodpeckerSchedule } from './woodpecker-stats.util';
import { parseColor, resolveColor, setDarkTheme, textContrast } from '../../testing/contrast';

// Wunsch 2026-11-18: „neben x pro Tag für fertig auch noch ein Checkmark, wenn ich % der Gesamtpuzzles passend zum
// aktuellen Tag hab (und in Statistik oben ausgewiesen: noch x Puzzle bis z. B. 55 % erfüllt)“ — EIN Baustein für
// Dashboard-Kachel, Löser-Kopf (kurz) und Statistik-Fenster (ausführlich).
describe('WoodpeckerScheduleComponent (Zeitplan, 0.994.0)', () => {
  const sched = (over: Partial<WoodpeckerSchedule> = {}): WoodpeckerSchedule => ({
    day: 8, days: 14, targetPct: 57, target: 155, attempted: 143, setSize: 270, currentPct: 53, left: 12, reached: false,
    overdue: false, ...over,
  });
  const done = (over: Partial<WoodpeckerSchedule> = {}) => sched({ attempted: 165, currentPct: 61, left: 0, reached: true, ...over });

  function make(schedule: WoodpeckerSchedule | null, full = false) {
    TestBed.configureTestingModule({
      imports: [WoodpeckerScheduleComponent],
      providers: [provideTranslateService({ fallbackLang: 'en' })],
    });
    const t = TestBed.inject(TranslateService);
    t.setTranslation('en', { woodpecker: { schedule: {
      label: 'Schedule', labelDay: 'Schedule (day {{k}} of {{d}})', onTrack: 'On schedule', left: '{{n}} more to reach {{pct}}',
      leftLong: '{{n}} more puzzles to reach {{pct}}', leftLongOne: '{{n}} more puzzle to reach {{pct}}',
      reachedPct: '{{pct}} reached', current: 'currently {{pct}}',
    } } });
    t.use('en');
    const fixture = TestBed.createComponent(WoodpeckerScheduleComponent);
    fixture.componentRef.setInput('schedule', schedule);
    fixture.componentRef.setInput('full', full);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    /** Die Teile in Lesereihenfolge; Haken und sein Text stehen in einer gemeinsamen Hülle (`.wsc-ok`). */
    const parts = () => Array.from(el.querySelectorAll('.wsc > :not(.wsc-ok), .wsc-ok > *')).map(x => x.textContent!.trim());
    return { fixture, el, parts };
  }

  describe('kurz (Kachel, Löser-Kopf)', () => {
    it('dahinter: leise „Zeitplan: noch 12 bis 57 %“ — kein Haken, keine Erfolgs- oder Warnfarbe', () => {
      const { el, parts } = make(sched());
      expect(parts()).toEqual(['Schedule:', '12 more to reach 57%']);
      expect(el.querySelector('mat-icon')).toBeNull();
      expect(el.querySelector('.wsc-done')).toBeNull();
      expect(el.querySelector('.wsc-reached')).toBeNull();
      // neutral: die Angabe erbt die Farbe der Zeile, sie färbt sich nicht selbst
      const left = el.querySelector('.wsc-left') as HTMLElement;
      expect(getComputedStyle(left).color).toBe(getComputedStyle(el).color);
    });

    it('im Zeitplan: Haken check_circle + Klartext „Im Zeitplan“ (nie nur Farbe), der Soll-Anteil dahinter', () => {
      const { el, parts } = make(done());
      expect(parts()).toEqual(['check_circle', 'On schedule', '57%']);
      const icon = el.querySelector('mat-icon.wsc-check') as HTMLElement;
      expect(icon.getAttribute('aria-hidden')).toBe('true');           // der Text daneben ist der zugängliche Name
      expect(el.querySelector('.wsc')!.classList).toContain('wsc-done');
      expect(el.querySelector('.wsc-label')).toBeNull();
    });

    it('nach der Frist: „noch 40 bis 100 %“', () => {
      const { parts } = make(sched({ day: 14, targetPct: 100, target: 270, attempted: 230, currentPct: 85, left: 40, overdue: true }));
      expect(parts()).toEqual(['Schedule:', '40 more to reach 100%']);
    });

    it('wechselt von dahinter auf im Zeitplan, sobald ein neuer Stand hereinkommt (OnPush, Signal-Eingabe)', () => {
      const { fixture, el, parts } = make(sched({ attempted: 154, left: 1 }));
      expect(el.querySelector('.wsc-check')).toBeNull();
      fixture.componentRef.setInput('schedule', done({ attempted: 155, currentPct: 57 }));
      fixture.detectChanges();
      expect(parts()).toEqual(['check_circle', 'On schedule', '57%']);
    });
  });

  describe('ausführlich (Statistik-Fenster)', () => {
    it('dahinter: „Zeitplan (Tag 8 von 14): noch 12 Aufgaben bis 57 %“', () => {
      const { el, parts } = make(sched(), true);
      expect(parts()).toEqual(['Schedule (day 8 of 14):', '12 more puzzles to reach 57%']);
      expect(el.querySelector('mat-icon')).toBeNull();
      expect(el.querySelector('.wsc')!.classList).toContain('wsc-full');
    });

    it('eine einzige fehlt: Einzahl', () => {
      expect(make(sched({ attempted: 154, left: 1 }), true).parts()[1]).toBe('1 more puzzle to reach 57%');
    });

    it('im Zeitplan: Haken + „57 % erreicht“ und der aktuelle Stand', () => {
      const { el, parts } = make(done(), true);
      expect(parts()).toEqual(['Schedule (day 8 of 14):', 'check_circle', '57% reached', '(currently 61%)']);
      expect(el.querySelector('mat-icon.wsc-check')?.getAttribute('aria-hidden')).toBe('true');
      // Haken und „57 % erreicht“ brechen nie auseinander (der Haken stünde sonst allein hinter der Beschriftung)
      const ok = el.querySelector('.wsc-ok') as HTMLElement;
      expect(Array.from(ok.children).map(x => x.className.split(' ').find(c => c.startsWith('wsc-')))).toEqual(['wsc-check', 'wsc-reached']);
      expect(getComputedStyle(ok).whiteSpace).toBe('nowrap');
      expect(getComputedStyle(ok).flexWrap).toBe('nowrap');
    });

    it('nach der Frist: ohne Tagesangabe, Soll 100 %', () => {
      const { parts } = make(sched({ day: 14, targetPct: 100, target: 270, attempted: 230, currentPct: 85, left: 40, overdue: true }), true);
      expect(parts()).toEqual(['Schedule:', '40 more puzzles to reach 100%']);
    });
  });

  it('ohne Zeitplan: nichts', () => {
    const { el } = make(null);
    expect(el.querySelector('.wsc')).toBeNull();
    expect(el.textContent!.trim()).toBe('');
  });

  it('Erfolgsfarbe aus dem Token: Haken und Text in --rh-success, hell und dunkel mit Kontrast ≥ 4,5:1', () => {
    const { el } = make(done());
    const label = el.querySelector('.wsc-reached') as HTMLElement;
    const icon = el.querySelector('.wsc-check') as HTMLElement;
    const was = setDarkTheme(false);
    try {
      for (const dark of [false, true]) {
        setDarkTheme(dark);
        const mode = dark ? 'dunkel' : 'hell';
        const token = resolveColor('var(--rh-success)');
        expect(parseColor(getComputedStyle(label).color)).withContext(mode).toEqual(token);
        expect(parseColor(getComputedStyle(icon).color)).withContext(mode).toEqual(token);
        expect(textContrast(label)).withContext(mode).toBeGreaterThanOrEqual(4.5);
      }
    } finally {
      setDarkTheme(was);
    }
  });
});

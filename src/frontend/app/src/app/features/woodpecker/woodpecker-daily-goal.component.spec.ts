import { TestBed } from '@angular/core/testing';
import { provideTranslateService, TranslateService } from '@ngx-translate/core';
import { WoodpeckerDailyGoalComponent } from './woodpecker-daily-goal.component';
import { WoodpeckerDailyGoal } from './woodpecker-stats.util';
import { parseColor, resolveColor, setDarkTheme, textContrast } from '../../testing/contrast';

// Wunsch 2026-11-18: „auch ohne Statistik visualisieren, wenn ich heute das notwendige Minimum erledigt habe … grüne
// Checkmark oder so vielleicht“ — EIN Baustein für Dashboard-Kachel, Löser-Kopf und Statistik-Fenster.
describe('WoodpeckerDailyGoalComponent (Tagesziel, 0.993.0)', () => {
  function make(goal: WoodpeckerDailyGoal | null) {
    TestBed.configureTestingModule({
      imports: [WoodpeckerDailyGoalComponent],
      providers: [provideTranslateService({ fallbackLang: 'en' })],
    });
    const t = TestBed.inject(TranslateService);
    t.setTranslation('en', { woodpecker: {
      overview: { todayLabel: 'Today' }, daily: { count: '{{today}} of {{target}}', reached: 'Daily goal reached' },
    } });
    t.use('en');
    const fixture = TestBed.createComponent(WoodpeckerDailyGoalComponent);
    fixture.componentRef.setInput('goal', goal);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const parts = () => Array.from(el.querySelectorAll('.wdg > *')).map(x => x.textContent!.trim());
    return { fixture, el, parts };
  }

  it('offen: leise „Heute: 23 von 45“ — kein Haken, keine Erfolgsfarbe', () => {
    const { el, parts } = make({ today: 23, target: 45, open: 247, days: 6, reached: false });
    expect(parts()).toEqual(['Today:', '23 of 45']);
    expect(el.querySelector('mat-icon')).toBeNull();
    expect(el.querySelector('.wdg-done')).toBeNull();
    expect(el.querySelector('.wdg-reached')).toBeNull();
  });

  it('erreicht: Haken check_circle + Klartext „Tagesziel erreicht“ (nie nur Farbe), die Zahl dahinter', () => {
    const { el, parts } = make({ today: 45, target: 45, open: 225, days: 6, reached: true });
    expect(parts()).toEqual(['check_circle', 'Daily goal reached', '45 of 45']);
    const icon = el.querySelector('mat-icon.wdg-check') as HTMLElement;
    expect(icon.getAttribute('aria-hidden')).toBe('true');           // der Text daneben ist der zugängliche Name
    expect(el.querySelector('.wdg')!.classList).toContain('wdg-done');
    expect(el.querySelector('.wdg-label')).toBeNull();
  });

  it('übererfüllt: bleibt „erreicht“ und zeigt die echte Zahl', () => {
    const { parts } = make({ today: 52, target: 45, open: 218, days: 6, reached: true });
    expect(parts()).toEqual(['check_circle', 'Daily goal reached', '52 of 45']);
  });

  it('Zyklus 1 (kein Soll): nur „Heute: 7“', () => {
    const { el, parts } = make({ today: 7, target: null, open: null, days: null, reached: false });
    expect(parts()).toEqual(['Today:', '7']);
    expect(el.querySelector('mat-icon')).toBeNull();
  });

  it('ohne Ziel: nichts', () => {
    const { el } = make(null);
    expect(el.querySelector('.wdg')).toBeNull();
    expect(el.textContent!.trim()).toBe('');
  });

  it('wechselt von offen auf erreicht, sobald ein neues Ziel hereinkommt (OnPush, Signal-Eingabe)', () => {
    const { fixture, el, parts } = make({ today: 44, target: 45, open: 226, days: 6, reached: false });
    expect(el.querySelector('.wdg-check')).toBeNull();
    fixture.componentRef.setInput('goal', { today: 45, target: 45, open: 225, days: 6, reached: true });
    fixture.detectChanges();
    expect(parts()).toEqual(['check_circle', 'Daily goal reached', '45 of 45']);
  });

  it('Erfolgsfarbe aus dem Token: Haken und Text in --rh-success, hell und dunkel mit Kontrast ≥ 4,5:1', () => {
    const { el } = make({ today: 45, target: 45, open: 225, days: 6, reached: true });
    const label = el.querySelector('.wdg-reached') as HTMLElement;
    const icon = el.querySelector('.wdg-check') as HTMLElement;
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

import { TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { CalcTimerDialogComponent, CalcTimerDialogData, CalcTimerDialogResult } from './calc-timer-dialog.component';

/** Dialog mit den übergebenen Daten bauen (`null` = ohne MAT_DIALOG_DATA); `closed` sammelt die Schließwerte. */
async function open(data: CalcTimerDialogData | null) {
  const closed: (CalcTimerDialogResult | undefined)[] = [];
  await TestBed.configureTestingModule({
    imports: [CalcTimerDialogComponent],
    providers: [
      provideNoopAnimations(),
      provideTranslateService({ fallbackLang: 'en' }),
      ...(data ? [{ provide: MAT_DIALOG_DATA, useValue: data }] : []),
      { provide: MatDialogRef, useValue: { close: (v?: CalcTimerDialogResult) => closed.push(v) } },
    ],
  }).compileComponents();
  const fixture = TestBed.createComponent(CalcTimerDialogComponent);
  fixture.detectChanges();
  const el = fixture.nativeElement as HTMLElement;
  const actions = () => el.querySelectorAll<HTMLButtonElement>('mat-dialog-actions button');
  return { fixture, c: fixture.componentInstance, closed, el, actions };
}

describe('CalcTimerDialogComponent', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('zerlegt die Kapitelzeit in Stunden, Minuten und Sekunden und nennt das Kapitel', async () => {
    const { c, el } = await open({ seconds: 3725, chapter: 'KW 7' });

    expect([c.hours, c.minutes, c.seconds]).toEqual([1, 2, 5]);
    expect(el.querySelectorAll('.ct-field input').length).toBe(3);
    // Ohne Übersetzungen steht der Schlüssel da — wichtig ist, dass der Dialog sagt, WESSEN Zeit es ist.
    expect(el.querySelector('.ct-where')!.textContent).toContain('calc.timer.editFor');
  });

  it('Felder sind Tippziele von mindestens 44 px und teilen sich die Breite (d7-rk-dialog-felder-unter-44px-mobil)', async () => {
    const { el } = await open({ seconds: 60, chapter: 'K' });
    document.body.appendChild(el);
    el.style.width = '300px';
    try {
      // Tippziel ist das ganze Outline-Feld (Rahmen), nicht nur die Textzeile darin.
      const boxes = Array.from(el.querySelectorAll<HTMLElement>('.ct-field .mat-mdc-text-field-wrapper'));
      expect(boxes.length).toBe(3);
      boxes.forEach(b => expect(b.getBoundingClientRect().height).toBeGreaterThanOrEqual(44));
      const fields = Array.from(el.querySelectorAll<HTMLElement>('.ct-field'));
      const widths = fields.map(f => Math.round(f.getBoundingClientRect().width));
      expect(Math.max(...widths) - Math.min(...widths)).toBeLessThanOrEqual(1);
      expect(getComputedStyle(fields[0]).flexGrow).toBe('1');
    } finally {
      el.remove();
    }
  });

  // d8-rk-kalk-kapitelzeit-felder-nicht-outline / d8-rk-kapitelzeit-eingabefelder-klein: Outline-Felder wie in den übrigen
  // RookHub-Dialogen (DESIGN 5.8), Beschriftung im Rahmen, Ziffern mindestens 16 px (DESIGN 3.5, sonst zoomt iOS).
  it('d8: Stunden/Minuten/Sekunden sind Outline-Felder mit mindestens 16 px Schrift', async () => {
    const { el } = await open({ seconds: 60, chapter: 'K' });
    document.body.appendChild(el);
    try {
      const fields = Array.from(el.querySelectorAll<HTMLElement>('mat-form-field.ct-field'));
      expect(fields.length).toBe(3);
      fields.forEach(f => expect(f.classList).toContain('mat-form-field-appearance-outline'));
      expect(Array.from(fields, f => f.querySelector('mat-label')?.textContent?.trim()))
        .toEqual(['calc.timer.hours', 'calc.timer.minutes', 'calc.timer.seconds']);
      el.querySelectorAll<HTMLInputElement>('.ct-field input').forEach(i =>
        expect(parseFloat(getComputedStyle(i).fontSize)).toBeGreaterThanOrEqual(16));
      expect(el.querySelector('label.ct-field')).toBeNull();
    } finally {
      el.remove();
    }
  });

  it('läuft ohne Dialogdaten mit null Sekunden an', async () => {
    const { c } = await open(null);
    expect([c.hours, c.minutes, c.seconds]).toEqual([0, 0, 0]);
    expect(c.data.chapter).toBe('');
  });

  it('zwingt Eingaben in den gültigen Bereich: negativ/leer/NaN → 0, zu groß → Obergrenze, Bruch → abgerundet', async () => {
    const { c } = await open({ seconds: 0, chapter: 'K' });

    c.hours = 120; c.minutes = -3; c.seconds = 12.9;
    c.clamp();
    expect([c.hours, c.minutes, c.seconds]).toEqual([99, 0, 12]);

    // Ein geleertes Zahlenfeld liefert null/'' — das ist keine Zeit, sondern 0.
    c.hours = null as unknown as number; c.minutes = '' as unknown as number; c.seconds = NaN;
    c.clamp();
    expect([c.hours, c.minutes, c.seconds]).toEqual([0, 0, 0]);

    c.minutes = 75; c.seconds = 60;
    c.clamp();
    expect([c.minutes, c.seconds]).toEqual([59, 59]);
  });

  it('Speichern schließt mit der GEKLEMMTEN Gesamtzeit in Sekunden', async () => {
    const { c, actions, closed } = await open({ seconds: 3725, chapter: 'K' });

    c.hours = 2; c.minutes = 75;            // ohne Klemme wären das 2:75:05
    actions()[1].click();                   // „Speichern"

    expect(closed).toEqual([2 * 3600 + 59 * 60 + 5]);
  });

  it('Abbrechen schließt mit `undefined` — nie mit dem leeren String', async () => {
    // Ein statisches mat-dialog-close-Attribut schlösse mit '' — der Aufrufer nähme das als gesetzte Zeit.
    const { actions, closed } = await open({ seconds: 900, chapter: 'K' });

    actions()[0].click();                   // „Abbrechen"

    expect(closed.length).toBe(1);
    expect(closed[0]).toBeUndefined();
    expect(closed[0] as unknown).not.toBe('');
  });
});

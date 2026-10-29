import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { PuzzleYourTurnComponent } from './puzzle-your-turn.component';

describe('PuzzleYourTurnComponent', () => {
  it('creates (template AOT-compiles + DI resolves)', async () => {
    await TestBed.configureTestingModule({
      imports: [PuzzleYourTurnComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(PuzzleYourTurnComponent);
    expect(fixture.componentInstance).toBeTruthy();
  });

  it('showStatus=false blendet NUR die Statuszeile aus (Timer + Aktionen bleiben)', async () => {
    await TestBed.configureTestingModule({
      imports: [PuzzleYourTurnComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(PuzzleYourTurnComponent);
    const el: HTMLElement = fixture.nativeElement;
    // setInput statt Feldzuweisung: die Komponente ist OnPush und würde sonst nicht neu rendern.
    fixture.componentRef.setInput('timerSeconds', 42);
    fixture.detectChanges();
    expect(el.querySelector('.ytp-status')).toBeTruthy();

    fixture.componentRef.setInput('showStatus', false);
    fixture.detectChanges();
    expect(el.querySelector('.ytp-status')).toBeNull();     // Doppelaussage zum Brett-Hinweis weg
    expect(el.querySelector('.ytp-timer')).toBeTruthy();    // Zeit bleibt
    expect(el.querySelectorAll('.ytp-actions button').length).toBeGreaterThan(0);
  });
  it('d3-rk-puzzle-zeitanzeige-ohne-beschriftung: die Zeit trägt Uhr-Symbol und „Lösezeit“ als Namen', async () => {
    await TestBed.configureTestingModule({
      imports: [PuzzleYourTurnComponent],
      providers: [provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' })],
    }).compileComponents();
    const fixture = TestBed.createComponent(PuzzleYourTurnComponent);
    fixture.componentRef.setInput('timerSeconds', 4);
    fixture.detectChanges();
    const timer = (fixture.nativeElement as HTMLElement).querySelector('.ytp-timer')!;
    expect(timer.querySelector('mat-icon')!.textContent!.trim()).toBe('timer');
    expect(timer.querySelector('mat-icon')!.getAttribute('aria-hidden')).toBe('true');
    expect(timer.getAttribute('role')).toBe('timer');
    expect(timer.getAttribute('aria-label')).toMatch(/^puzzles\.solveTime: \S+/);
  });
});


describe('PuzzleYourTurnComponent Überspringen neben Aufgeben (2026-10-20)', () => {
  it('zeigt „Überspringen“ vor „Aufgeben“ nur mit showSkip und meldet den Klick', () => {
    TestBed.configureTestingModule({ imports: [PuzzleYourTurnComponent], providers: [provideTranslateService({ fallbackLang: 'en' })] });
    const f = TestBed.createComponent(PuzzleYourTurnComponent);
    f.componentRef.setInput('state', 'AWAITING_USER_MOVE');
    f.detectChanges();
    expect(f.nativeElement.querySelector('.ytp-skip')).toBeNull();
    f.componentRef.setInput('showSkip', true);
    f.detectChanges();
    const skip = f.nativeElement.querySelector('.ytp-skip') as HTMLButtonElement;
    expect(skip).not.toBeNull();
    const btns = [...f.nativeElement.querySelectorAll('.ytp-actions button')] as HTMLElement[];
    expect(btns.indexOf(skip)).toBe(btns.length - 2);           // direkt vor „Aufgeben“
    let n = 0; f.componentInstance.skipClicked.subscribe(() => n++);
    skip.click();
    expect(n).toBe(1);
  });

  // d8-rk-zeitkarte-polster-unten: die Textknöpfe unten bringen ≈ 8 px unsichtbaren Rand mit — das Polster unten ist um
  // genau diese 8 px kleiner, damit der sichtbare Abstand oben und unten gleich ist (DESIGN 5.2).
  it('d8: unten 8 px weniger Polster als oben, die letzte Zeile sind Textknöpfe', async () => {
    await TestBed.configureTestingModule({
      imports: [PuzzleYourTurnComponent],
      providers: [provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' })],
    }).compileComponents();
    const fixture = TestBed.createComponent(PuzzleYourTurnComponent);
    fixture.componentRef.setInput('timerSeconds', 42);
    fixture.detectChanges();
    const el: HTMLElement = fixture.nativeElement;
    document.body.appendChild(el);
    try {
      const center = el.querySelector<HTMLElement>('.ytp-center')!;
      const s = getComputedStyle(center);
      expect(parseFloat(s.paddingTop) - parseFloat(s.paddingBottom)).toBe(8);
      expect(center.lastElementChild!.classList).toContain('ytp-actions');
      const buttons = Array.from(center.lastElementChild!.querySelectorAll('button'));
      expect(buttons.length).toBeGreaterThan(0);
      buttons.forEach(b => expect(b.classList).toContain('mat-mdc-button'));   // Textknöpfe, kein Rahmen/keine Fläche
    } finally { el.remove(); }
  });
});

import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TranslateService, provideTranslateService } from '@ngx-translate/core';
import { GameRecapComponent } from './game-recap.component';

const LONG = 'In einer ausgeglichenen Partie setzte sich kahalm gegen ukker1992 durch. Nach dem Fehler 19.fxg6 und der '
  + 'Antwort 19…Dxd3 geriet Weiß kurz in Not, fand aber mit 23.Tf7 eine starke Fortsetzung, die den schwarzen König '
  + 'entblößte; im Endspiel entschied der Mehrbauer, und nach 41 Zügen gab Schwarz auf.';

describe('GameRecapComponent', () => {
  let fixture: ComponentFixture<GameRecapComponent>;
  let host: HTMLElement;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [GameRecapComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideTranslateService({ fallbackLang: 'en' })],
    }).compileComponents();
    const t = TestBed.inject(TranslateService);
    t.setTranslation('de', { common: { showMore: 'Mehr', showLess: 'Weniger' } });
    t.use('de');
    fixture = TestBed.createComponent(GameRecapComponent);
    host = fixture.nativeElement as HTMLElement;
    host.style.width = '358px';
    document.body.appendChild(host);
  });

  afterEach(() => host.remove());

  function render(text: string): HTMLElement {
    fixture.componentRef.setInput('text', text);
    fixture.detectChanges();
    return host.querySelector('p.recap') as HTMLElement;
  }

  function lineCount(p: HTMLElement): number {
    const cs = getComputedStyle(p);
    const inner = p.getBoundingClientRect().height - parseFloat(cs.paddingTop) - parseFloat(cs.paddingBottom);
    return Math.round(inner / parseFloat(cs.lineHeight));
  }

  // Nebenbefund der Nachprüfung v1: eingeklappt war kein „Mehr" zu sehen — nur Antippen half, ohne Hinweis.
  it('zeigt einen langen Text auf drei Zeilen mit sichtbarem „Mehr" (aria-expanded) und klappt ihn auf', () => {
    const p = render(LONG);
    const more = p.querySelector('button.clamp-more') as HTMLButtonElement;
    expect(more.hidden).toBeFalse();
    expect(more.textContent!.trim()).toBe('Mehr');
    expect(more.getAttribute('aria-expanded')).toBe('false');
    expect(more.getAttribute('aria-controls')).toBe(p.id);
    expect(lineCount(p)).toBeLessThanOrEqual(3);
    expect(p.querySelector('.clamp-text')!.textContent!.endsWith(' …')).toBeTrue();

    more.click();
    fixture.detectChanges();
    expect(p.classList).toContain('open');
    expect(more.getAttribute('aria-expanded')).toBe('true');
    expect(more.textContent!.trim()).toBe('Weniger');
    expect(p.querySelector('.clamp-text')!.textContent).toBe(LONG);
    expect(lineCount(p)).toBeGreaterThan(3);

    more.click();
    fixture.detectChanges();
    expect(p.classList).not.toContain('open');
    expect(lineCount(p)).toBeLessThanOrEqual(3);
  });

  it('Antippen des Kastens klappt weiterhin auf und zu', () => {
    const p = render(LONG);
    p.click();
    fixture.detectChanges();
    expect(p.classList).toContain('open');
    p.click();
    fixture.detectChanges();
    expect(p.classList).not.toContain('open');
  });

  it('kurzer Text: ganz, ohne „Mehr", Antippen ändert nichts', () => {
    const p = render('b gewinnt nach 1...c5.');
    expect(p.querySelector('.clamp-text')!.textContent).toBe('b gewinnt nach 1...c5.');
    expect((p.querySelector('.clamp-more') as HTMLElement).hidden).toBeTrue();
    p.click();
    fixture.detectChanges();
    expect(p.classList).not.toContain('open');
  });

  it('eigene Partie: holt den Text erst nach fertiger Analyse', () => {
    const http = TestBed.inject(HttpTestingController);
    fixture.componentRef.setInput('own', true);
    fixture.componentRef.setInput('gameId', 4);
    fixture.detectChanges();
    http.expectNone('/api/games/4/recap');
    fixture.componentRef.setInput('analysisDone', true);
    fixture.detectChanges();
    http.expectOne('/api/games/4/recap').flush({ available: true, hasAnalysis: true, text: 'Kurz.', pending: false });
    fixture.detectChanges();
    expect(host.querySelector('p.recap .clamp-text')!.textContent).toBe('Kurz.');
    expect(host.classList).toContain('has-text');
    http.verify();
  });
});

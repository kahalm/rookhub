import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { StatsComponent } from './stats.component';

describe('StatsComponent', () => {
  it('creates (template AOT-compiles + DI resolves)', async () => {
    await TestBed.configureTestingModule({
      imports: [StatsComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(StatsComponent);
    expect(fixture.componentInstance).toBeTruthy();
  });

  it('laesst die Rating-Baender IM Container scrollen statt die Seite zu verbreitern', async () => {
    // Regression (Hochformat, gemeldet 2026-09-05): 200er-Baender ueber die ganze Rating-Spanne
    // ergeben 14–18 Saeulen. Ohne eigenes overflow-x wuchs die SEITE um rund zwei Bildschirme nach
    // rechts — und weil CDK-Overlays gegen das Dokument rechnen, landeten die Untermenues des
    // Hamburger-Menues ausserhalb des Sichtfelds. Der Test misst genau das: schmaler Host,
    // viele Baender, Dokument darf NICHT breiter werden.
    await TestBed.configureTestingModule({
      imports: [StatsComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(StatsComponent);
    const host = fixture.nativeElement as HTMLElement;
    // Hochkant-Handy nachstellen (iPhone-Breite), damit die Messung nicht vom Karma-Fenster abhaengt.
    host.style.width = '390px';
    host.style.overflowX = 'hidden';   // wie ein Viewport: was rauslaeuft, wuerde hier scrollen

    // ERST rendern (dabei laeuft ngOnInit und setzt `loading` selbst auf true; die HTTP-Aufrufe
    // bleiben im Testing-Backend offen), DANN den Zustand setzen — umgekehrt ueberschreibt
    // ngOnInit die Testdaten und es rendert nur der Spinner.
    fixture.detectChanges();
    (fixture.componentInstance as unknown as { loading: boolean }).loading = false;
    fixture.componentInstance.ratingBands = Array.from({ length: 18 }, (_, i) => ({
      from: 400 + i * 200, to: 599 + i * 200, attempts: 120, solved: 1234,
    }));
    fixture.detectChanges();

    const bands = host.querySelector('.bands') as HTMLElement | null;
    expect(bands).withContext('Baender-Streifen gerendert').toBeTruthy();
    // Der Streifen selbst ist breiter als der Platz — genau deshalb MUSS er scrollen koennen …
    expect(bands!.scrollWidth).toBeGreaterThan(bands!.clientWidth);
    expect(getComputedStyle(bands!).overflowX).toBe('auto');
    // … und darf die Karte/den Container NICHT aufblaehen.
    const container = host.querySelector('.stats-container') as HTMLElement;
    expect(container.scrollWidth).toBeLessThanOrEqual(container.clientWidth + 1);
  });
});

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

  it('zeigt in BEIDEN Modi die Zahlen der jeweils gewaehlten Statistik (`current`)', async () => {
    // Die fuenf Kacheln lasen bis 0.499.5 fuenf einzelne Getter, jeder mit derselben
    // Modus-Weiche. Jetzt ist es EIN `current` — der Test haelt fest, dass das Umschalten
    // wirklich die andere Quelle zeigt und nicht etwa beide Modi dieselben Zahlen bekommen.
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
    const c = fixture.componentInstance;
    fixture.detectChanges();
    (c as unknown as { loading: boolean }).loading = false;

    c.stats = {
      totalAttempts: 40, solved: 30, accuracy: 75, currentStreak: 3, bestStreak: 9, puzzleElo: 1620,
    };
    c.courseStats = {
      totalAttempts: 7, solved: 5, accuracy: 71.4, currentStreak: 1, bestStreak: 2,
    };

    c.mode = 'standard';
    fixture.detectChanges();
    expect(c.current).toBe(c.stats);
    let values = Array.from(fixture.nativeElement.querySelectorAll('.cards .stat .val')).map(e => (e as HTMLElement).textContent!.trim());
    // Im Standard-Modus steht die Elo-Kachel VOR den fuenf gemeinsamen.
    expect(values).toEqual(['1620', '30', '40', '75%', '3', '9']);

    c.mode = 'course';
    fixture.detectChanges();
    expect(c.current).toBe(c.courseStats);
    values = Array.from(fixture.nativeElement.querySelectorAll('.cards .stat .val')).map(e => (e as HTMLElement).textContent!.trim());
    expect(values).toEqual(['5', '7', '71.4%', '1', '2']);   // keine Elo-Kachel
  });
});

import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { firstValueFrom } from 'rxjs';
import { TranslateService, provideTranslateService } from '@ngx-translate/core';
import { MaximsComponent } from './maxims.component';
import { ChessMaxim } from '../../shared/maxims/chess-maxims.service';

const LIST: ChessMaxim[] = [
  { id: 'springer-am-rand', text: 'Springer am Rand bringt Kummer und Schand.', category: 'Figuren', when: 'Springer auf a/h ohne Grund.',
    sources: [{ ebookId: 1314, author: 'Jeremy Silman', title: 'Schach mit neuem Schwung', location: 'PDF-S. 12', page: 12, accessible: true }] },
  { id: 'ungedeckte-figuren', text: 'Ungedeckte Figuren fallen.', category: 'Taktik', when: 'Eine Figur steht ungedeckt.',
    sources: [{ ebookId: 769, author: 'John Nunn', title: 'Secrets of Practical Chess', location: 'PDF-S. 40', page: 40, quote: 'Loose pieces drop off', accessible: false }] },
];

describe('MaximsComponent (0.917.0)', () => {
  it('listet je Kategorie aufklappbar, verlinkt nur zugängliche Bücher', () => {
    TestBed.configureTestingModule({
      imports: [MaximsComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]), provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' })],
    });
    const f = TestBed.createComponent(MaximsComponent);
    f.detectChanges();
    TestBed.inject(HttpTestingController).expectOne('/api/maxims').flush(LIST);
    f.detectChanges();
    const el = f.nativeElement as HTMLElement;
    expect(Array.from(el.querySelectorAll('h2')).map(h => h.textContent)).toEqual(['Figuren', 'Taktik']);
    expect(el.querySelectorAll('mat-expansion-panel').length).toBe(2);
    const links = Array.from(el.querySelectorAll('.sources a')) as HTMLAnchorElement[];
    expect(links.map(a => a.getAttribute('href'))).toEqual(['/ebooks/1314?page=12&fmt=pdf']);
    expect(el.textContent).toContain('Secrets of Practical Chess');
    expect(el.textContent).toContain('maxims.noAccess');
  });

  it('zeigt Satz, Kategorie und „wann“ in der Oberflächensprache, Zitate bleiben im Original', async () => {
    TestBed.configureTestingModule({
      imports: [MaximsComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]), provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' })],
    });
    const f = TestBed.createComponent(MaximsComponent);
    f.detectChanges();
    TestBed.inject(HttpTestingController).expectOne('/api/maxims').flush(LIST.map(m => ({
      ...m, textEn: 'EN ' + m.id, categoryEn: 'Cat ' + m.category, whenEn: 'When ' + m.id,
    })));
    const translate = TestBed.inject(TranslateService);
    const el = f.nativeElement as HTMLElement;
    const heads = () => Array.from(el.querySelectorAll('h2')).map(h => h.textContent);

    await firstValueFrom(translate.use('de'));
    f.detectChanges();
    expect(heads()).toEqual(['Figuren', 'Taktik']);
    expect(el.textContent).toContain('Springer am Rand bringt Kummer und Schand.');

    await firstValueFrom(translate.use('hr'));
    f.detectChanges();
    expect(heads()).toEqual(['Cat Figuren', 'Cat Taktik']);
    expect(el.textContent).toContain('EN springer-am-rand');
    expect(el.textContent).not.toContain('Springer am Rand bringt');
    expect(el.textContent).toContain('Loose pieces drop off');
  });

  it('Ladefehler zeigt app-load-error mit Wiederholen statt „0 Merksätze“ (F3-003)', () => {
    TestBed.configureTestingModule({
      imports: [MaximsComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]), provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' })],
    });
    const f = TestBed.createComponent(MaximsComponent);
    f.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/maxims').flush('x', { status: 502, statusText: 'Bad Gateway' });
    f.detectChanges();
    const el = f.nativeElement as HTMLElement;
    expect(el.querySelector('app-load-error')).not.toBeNull();
    expect(el.textContent).not.toContain('maxims.count');
    (el.querySelector('app-load-error button') as HTMLButtonElement).click();
    http.expectOne('/api/maxims').flush(LIST);
    f.detectChanges();
    expect(el.querySelector('app-load-error')).toBeNull();
    expect(el.querySelectorAll('mat-expansion-panel').length).toBe(2);
  });
});

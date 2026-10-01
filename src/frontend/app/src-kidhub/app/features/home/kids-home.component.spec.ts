import { TestBed, fakeAsync, tick } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { KidsHomeComponent } from './kids-home.component';

/**
 * Codereview 2026-09-29, A10-003: 20 Kinder öffnen im Vereinsraum hinter EINER NAT-Adresse KidHub — ab dem 16. kam
 * auf `levels` ein 429, und die Startseite zeigte das Fehlerbild statt der Stufen. Mit dem ECHTEN KidsApiService.
 */
describe('KidsHomeComponent', () => {
  const KEY = 'rh-kids-progress-v1';
  let http: HttpTestingController;

  beforeEach(() => {
    localStorage.removeItem(KEY);
    TestBed.configureTestingModule({
      imports: [KidsHomeComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]), provideTranslateService({ fallbackLang: 'en' })],
    });
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => {
    http.verify();
    localStorage.removeItem(KEY);
  });

  it('holt die Stufen nach einem 429 einmal nach, statt das Fehlerbild zu zeigen', fakeAsync(() => {
    const f = TestBed.createComponent(KidsHomeComponent);
    f.detectChanges();
    http.expectOne(r => r.url === '/api/kids/courses').flush([]);
    http.expectOne('/api/kids/levels').flush(null, { status: 429, statusText: 'Too Many Requests', headers: { 'Retry-After': '5' } });
    f.detectChanges();
    expect(f.componentInstance.failed()).toBeFalse();

    tick(5000);
    http.expectOne('/api/kids/levels').flush([{ level: 1, theme: 'mate1', puzzleCount: 10 }]);
    f.detectChanges();

    expect(f.componentInstance.failed()).toBeFalse();
    expect(f.componentInstance.current()).toBe(1);
    const el = f.nativeElement as HTMLElement;
    expect(el.querySelector('a.go')).not.toBeNull();
    expect(el.querySelector('kid-error')).toBeNull();
  }));

  it('ein zweites 429 zeigt das Fehlerbild (nur EIN Nachholversuch)', fakeAsync(() => {
    const f = TestBed.createComponent(KidsHomeComponent);
    f.detectChanges();
    http.expectOne(r => r.url === '/api/kids/courses').flush([]);
    http.expectOne('/api/kids/levels').flush(null, { status: 429, statusText: 'Too Many Requests', headers: { 'Retry-After': '1' } });
    tick(1000);
    http.expectOne('/api/kids/levels').flush(null, { status: 429, statusText: 'Too Many Requests', headers: { 'Retry-After': '1' } });
    f.detectChanges();
    expect(f.componentInstance.failed()).toBeTrue();
    expect((f.nativeElement as HTMLElement).querySelector('kid-error')).not.toBeNull();
  }));

  /** Codereview 2026-09-29, F7-011: im Fehlerfall stand nur ein Satz da — ohne Knopf, mit dem das Kind weiterkommt. */
  it('Fehlerbild mit „Nochmal": holt die Stufen neu, danach steht der Startknopf da', fakeAsync(() => {
    const f = TestBed.createComponent(KidsHomeComponent);
    f.detectChanges();
    http.expectOne(r => r.url === '/api/kids/courses').flush([]);
    http.expectOne('/api/kids/levels').flush(null, { status: 500, statusText: 'Server Error' });
    f.detectChanges();
    const el = f.nativeElement as HTMLElement;
    const again = el.querySelector<HTMLButtonElement>('kid-error button.again');
    expect(again).withContext('Knopf „Nochmal"').not.toBeNull();

    again!.click();
    // Die Kurse kamen schon — die liefert der Zwischenspeicher, neu geholt werden nur die Stufen.
    http.expectOne('/api/kids/levels').flush([{ level: 1, theme: 'mate1', puzzleCount: 10 }]);
    f.detectChanges();

    expect(f.componentInstance.failed()).toBeFalse();
    expect(el.querySelector('kid-error')).toBeNull();
    expect(el.querySelector('a.go')).not.toBeNull();
  }));
});

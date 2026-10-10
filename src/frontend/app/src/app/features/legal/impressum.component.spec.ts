import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideTranslateService } from '@ngx-translate/core';
import { ImpressumComponent } from './impressum.component';

describe('ImpressumComponent', () => {
  it('renders (template compiles)', async () => {
    await TestBed.configureTestingModule({
      imports: [ImpressumComponent],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting(), provideTranslateService({ fallbackLang: 'en' })],
    }).compileComponents();
    const f = TestBed.createComponent(ImpressumComponent);
    f.detectChanges();
    expect(f.componentInstance).toBeTruthy();
  });

  it('zeigt nur den Kontakt: rookhub@oberschm.id, kein Diensteanbieter-Block, kein Platzhalter-Hinweis', async () => {
    await TestBed.configureTestingModule({
      imports: [ImpressumComponent],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting(), provideTranslateService({ fallbackLang: 'en' })],
    }).compileComponents();
    const f = TestBed.createComponent(ImpressumComponent);
    f.detectChanges();
    const el = f.nativeElement as HTMLElement;
    const hrefs = Array.from(el.querySelectorAll('a')).map(a => a.getAttribute('href'));
    const text = el.textContent ?? '';
    expect(hrefs).toContain('mailto:rookhub@oberschm.id');
    expect(text).toContain('rookhub@oberschm.id');
    expect(text).toContain('legal.impressum.contactTitle');
    // Entscheidung des Betreibers (2026-09-30): kein Abschnitt „Diensteanbieter", keine Platzhalter.
    expect(text).not.toContain('legal.impressum.operatorTitle');
    expect(text).not.toContain('legal.impressum.disclaimer');
    expect(text).not.toContain('[');
    expect(text).not.toContain('cp-solutions');
  });

  it('Titel als h1, Abschnitte als h2 — keine uebersprungene Ebene (UX-057)', () => {
    TestBed.configureTestingModule({
      imports: [ImpressumComponent],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting(), provideTranslateService({ fallbackLang: 'en' })],
    });
    const f = TestBed.createComponent(ImpressumComponent);
    f.detectChanges();
    const el = f.nativeElement as HTMLElement;
    expect(el.querySelectorAll('h1').length).toBe(1);
    expect(el.querySelector('h1')?.textContent).toContain('legal.impressum.title');
    expect(el.querySelectorAll('h2').length).toBeGreaterThan(0);
    expect(el.querySelectorAll('h3, h4, h5, h6').length).toBe(0);
  });
});

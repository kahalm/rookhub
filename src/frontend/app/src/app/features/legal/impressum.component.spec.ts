import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { ImpressumComponent } from './impressum.component';

describe('ImpressumComponent', () => {
  it('renders (template compiles)', async () => {
    await TestBed.configureTestingModule({
      imports: [ImpressumComponent],
      providers: [provideRouter([]), provideTranslateService({ fallbackLang: 'en' })],
    }).compileComponents();
    const f = TestBed.createComponent(ImpressumComponent);
    f.detectChanges();
    expect(f.componentInstance).toBeTruthy();
  });

  it('zeigt nur den Kontakt: rookhub@oberschm.id, kein Diensteanbieter-Block, kein Platzhalter-Hinweis', async () => {
    await TestBed.configureTestingModule({
      imports: [ImpressumComponent],
      providers: [provideRouter([]), provideTranslateService({ fallbackLang: 'en' })],
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
});

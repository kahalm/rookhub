import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { of } from 'rxjs';
import { AnalyzeGameService, AnalyzeMode } from '../games/analyze-game.service';
import { AnalyzeChoiceCardComponent } from './analyze-choice-card.component';

/** Kompiliertes CSS ohne Kapselungs-Platzhalter, Leerraum vereinheitlicht. */
const css = () => (((AnalyzeChoiceCardComponent as unknown as { ɵcmp: { styles?: string[] } }).ɵcmp.styles) ?? []).join('\n')
  .replace(/%NS%/g, '').replace(/\[_ngcontent-%COMP%\]/g, '').replace(/\s+/g, ' ');

describe('AnalyzeChoiceCardComponent (d6)', () => {
  function render(width: string) {
    TestBed.configureTestingModule({
      imports: [AnalyzeChoiceCardComponent],
      providers: [
        provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }),
        { provide: AnalyzeGameService, useValue: { mode: signal<AnalyzeMode>('engine'), loadMode: () => of('engine'), setMode: () => {} } },
      ],
    });
    const fixture = TestBed.createComponent(AnalyzeChoiceCardComponent);
    const host = fixture.nativeElement as HTMLElement;
    host.style.display = 'block';
    host.style.width = width;
    document.body.appendChild(host);
    fixture.detectChanges();
    return { fixture, host };
  }

  afterEach(() => document.querySelectorAll('app-analyze-choice-card').forEach(e => e.remove()));

  it('d6-rk-profil-analysieren-umschalter-umbruch: drei Segmente in EINER Zeile (390 px), ohne Häkchen', () => {
    const { host } = render('390px');
    const toggles = Array.from(host.querySelectorAll<HTMLElement>('mat-button-toggle'));
    expect(toggles.length).toBe(3);
    const tops = toggles.map(t => t.offsetTop);
    expect(new Set(tops).size).withContext(`offsetTop ${tops.join('/')}`).toBe(1);
    expect(host.querySelector('.mat-button-toggle-checkbox-wrapper')).withContext('kein Häkchen').toBeNull();
  });

  it('d6-rk-profil-analysieren-umschalter-umbruch: kein flex-wrap; am Handy gleich breite Segmente, Symbol über Wort', () => {
    const s = css();
    expect(s).not.toContain('flex-wrap');
    const phone = s.slice(s.indexOf('@media (max-width: 599.98px)'));
    expect(phone).toMatch(/\.analyze-choice-toggle ?\{ ?display: flex; width: 100%/);
    expect(phone).toMatch(/\.analyze-choice-toggle mat-button-toggle ?\{ ?flex: 1 1 0/);
    expect(phone).toMatch(/\.mat-button-toggle-label-content ?\{ ?display: flex; flex-direction: column/);
  });

  it('d6-rk-profil-ueberschrift-partie-analysieren: Abschnittstitel in der Akzentfarbe', () => {
    expect(css()).toMatch(/\.analyze-choice-section h4 ?\{[^}]*color: var\(--rh-accent\)/);
  });
});

import { TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA } from '@angular/material/dialog';
import { provideTranslateService, TranslateService } from '@ngx-translate/core';
import { EndlessHelpDialogComponent } from './endless-help-dialog.component';

describe('EndlessHelpDialogComponent', () => {
  it('setzt die Phasengrenzen in die Fasttrack-Texte ein', () => {
    TestBed.configureTestingModule({
      imports: [EndlessHelpDialogComponent],
      providers: [
        provideTranslateService({ fallbackLang: 'en' }),
        { provide: MAT_DIALOG_DATA, useValue: { p1End: 10, p2Start: 11, p2End: 25, p3Start: 26, flatStep: 15 } },
      ],
    });
    const translate = TestBed.inject(TranslateService);
    translate.setTranslation('en', { endless: { help: {
      title: 'Help', fasttrackPhase1: 'P1 bis {{p1End}}', fasttrackPhase2: 'P2 {{p2Start}}–{{p2End}}',
      fasttrackPhase3: 'P3 ab {{p3Start}} (+{{flatStep}})',
    } } });
    translate.use('en');
    const fixture = TestBed.createComponent(EndlessHelpDialogComponent);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('h2')!.textContent).toContain('Help');
    const items = Array.from(el.querySelectorAll('li')).map(li => li.textContent!.trim());
    expect(items).toContain('P1 bis 10');
    expect(items).toContain('P2 11–25');
    expect(items).toContain('P3 ab 26 (+15)');
    expect(el.querySelector('button[mat-dialog-close]')!.getAttribute('aria-label')).toBe('common.close');
  });
  it('kein Leerband unter dem Titel: Inhalt ohne Polster oben, erste Überschrift ohne Abstand (d7-rk-hilfe-dialog-leerraum-unter-titel)', () => {
    TestBed.configureTestingModule({
      imports: [EndlessHelpDialogComponent],
      providers: [
        provideTranslateService({ fallbackLang: 'en' }),
        { provide: MAT_DIALOG_DATA, useValue: { p1End: 10, p2Start: 11, p2End: 25, p3Start: 26, flatStep: 15 } },
      ],
    });
    const fixture = TestBed.createComponent(EndlessHelpDialogComponent);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    document.body.appendChild(el);
    try {
      const body = el.querySelector<HTMLElement>('.help-body')!;
      expect(getComputedStyle(body).paddingTop).toBe('0px');
      const h3s = el.querySelectorAll<HTMLElement>('.help-body h3');
      expect(getComputedStyle(h3s[0]).marginTop).toBe('0px');
      expect(getComputedStyle(h3s[1]).marginTop).not.toBe('0px');   // spätere Abschnitte behalten ihren Abstand
    } finally {
      el.remove();
      TestBed.resetTestingModule();
    }
  });
});

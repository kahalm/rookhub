import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { AccountDeletionComponent } from './account-deletion.component';
import { LEGAL_SITE } from './legal-site';

describe('AccountDeletionComponent', () => {
  it('renders (template compiles)', async () => {
    await TestBed.configureTestingModule({
      imports: [AccountDeletionComponent],
      providers: [provideRouter([]), provideTranslateService({ fallbackLang: 'en' })],
    }).compileComponents();
    const f = TestBed.createComponent(AccountDeletionComponent);
    f.detectChanges();
    expect(f.componentInstance).toBeTruthy();
  });

  it('nennt den Kontakt der Oberflaeche (KidHub: eigene Adresse)', () => {
    TestBed.configureTestingModule({
      imports: [AccountDeletionComponent],
      providers: [provideRouter([]), provideTranslateService({ fallbackLang: 'en' }),
        { provide: LEGAL_SITE, useValue: { contactEmail: 'kidhub@oberschm.id', imprint: false } }],
    });
    const f = TestBed.createComponent(AccountDeletionComponent);
    f.detectChanges();
    expect((f.nativeElement as HTMLElement).querySelector('a[href="mailto:kidhub@oberschm.id"]')).not.toBeNull();
  });

  it('Ruecklink je Oberflaeche: RookHub zur Anmeldung, KidHub zur Startseite (F7-003)', () => {
    const render = (site?: object) => {
      TestBed.resetTestingModule();
      TestBed.configureTestingModule({
        imports: [AccountDeletionComponent],
        providers: [provideRouter([]), provideTranslateService({ fallbackLang: 'en' }),
          ...(site ? [{ provide: LEGAL_SITE, useValue: site }] : [])],
      });
      const f = TestBed.createComponent(AccountDeletionComponent);
      f.detectChanges();
      return f.nativeElement as HTMLElement;
    };
    expect(render().querySelector('.back a')?.getAttribute('href')).toBe('/login');
    const kid = render({ contactEmail: 'kidhub@oberschm.id', imprint: false, kind: 'kidhub', back: '/' });
    expect(kid.querySelector('.back a')?.getAttribute('href')).toBe('/');
    expect(kid.textContent).toContain('legal.backHome');
  });
});

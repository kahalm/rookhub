import { of, throwError } from 'rxjs';
import { ForgotPasswordComponent, resetMailSite } from './forgot-password.component';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { AuthService } from '../../core/auth.service';
import { SnackbarService } from '../../core/snackbar.service';
import { LEGAL_SITE } from '../legal/legal-site';
import { OPERATOR } from '../../../environments/operator';

describe('ForgotPasswordComponent', () => {
  function make(forgotReturn = of(void 0), legal?: any) {
    const auth: any = { forgotPassword: jasmine.createSpy('forgotPassword').and.returnValue(forgotReturn) };
    const snackbar: any = { warn: jasmine.createSpy('warn') };
    const translate: any = { instant: (k: string) => k, currentLang: () => 'de' };
    const c = new ForgotPasswordComponent(auth, snackbar, translate, legal);
    return { c, auth, snackbar };
  }

  it('trimmt die Email und ruft den Service', () => {
    const { c, auth } = make();
    c.email = '  user@test.com  ';
    c.onSubmit();
    expect(auth.forgotPassword.calls.mostRecent().args[0]).toBe('user@test.com');
  });

  // UX-031: die Mail soll die Seite verlinken, von der die Anfrage kam, und in deren Sprache kommen.
  it('schickt Seite und Sprache der Oberfläche mit (KidHub)', () => {
    const { c, auth } = make(of(void 0), { contactEmail: 'kidhub@oberschm.id', imprint: false, kind: 'kidhub' });
    c.email = 'mama@example.com';
    c.onSubmit();
    expect(auth.forgotPassword).toHaveBeenCalledWith('mama@example.com', 'kidhub', 'de');
  });

  it('RookHub (Vorgabe, nicht auf der Turnierseite): keine Seite — die Mail bleibt wie bisher', () => {
    const { c, auth } = make();
    c.email = 'user@test.com';
    c.onSubmit();
    // Karma laeuft auf localhost: kein Turnier-Host, also RookHub.
    expect(auth.forgotPassword).toHaveBeenCalledWith('user@test.com', null, 'de');
  });

  it('zeigt nach Erfolg die neutrale Bestätigung statt des Formulars', () => {
    const { c } = make(of(void 0));
    c.email = 'user@test.com';
    c.onSubmit();
    expect(c.sent).toBeTrue();
    expect(c.loading).toBeFalse();
  });

  it('warnt bei Fehler und bleibt im Formular', () => {
    const { c, snackbar } = make(throwError(() => ({ error: { message: 'boom' } })));
    c.email = 'user@test.com';
    c.onSubmit();
    expect(c.sent).toBeFalse();
    expect(snackbar.warn).toHaveBeenCalledWith('boom');
  });
});

/**
 * Gerendertes Template: autocomplete="email" laesst die Handy-Tastatur/den Passwort-Manager die Adresse anbieten.
 */
describe('ForgotPasswordComponent Template (Mobil-Attribute)', () => {
  it('setzt autocomplete="email" auf dem E-Mail-Feld', async () => {
    await TestBed.configureTestingModule({
      imports: [ForgotPasswordComponent],
      providers: [
        provideRouter([]),
        provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
        { provide: AuthService, useValue: {} },
        { provide: SnackbarService, useValue: {} },
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(ForgotPasswordComponent);
    fixture.detectChanges();
    const el: HTMLElement = fixture.nativeElement;
    expect(el.querySelector('input[name="email"]')!.getAttribute('autocomplete')).toBe('email');
  });
});

/**
 * UX-002: ein Konto ohne E-Mail bekommt hier nie eine Mail, und die Bestaetigung ist bewusst neutral. Die Seite
 * nennt deshalb den anderen Weg — den Kontakt der jeweiligen Oberflaeche, vor UND nach dem Absenden.
 */
describe('ForgotPasswordComponent — Konten ohne E-Mail (UX-002)', () => {
  async function render(legal?: object) {
    await TestBed.configureTestingModule({
      imports: [ForgotPasswordComponent],
      providers: [
        provideRouter([]),
        provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
        { provide: AuthService, useValue: {} },
        { provide: SnackbarService, useValue: {} },
        ...(legal ? [{ provide: LEGAL_SITE, useValue: legal }] : []),
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(ForgotPasswordComponent);
    fixture.detectChanges();
    return fixture;
  }

  it('nennt im Formular den Kontakt für Konten ohne E-Mail', async () => {
    const el: HTMLElement = (await render()).nativeElement;
    const line = el.querySelector('.no-email')!;
    expect(line).not.toBeNull();
    expect(line.textContent).toContain('auth.forgot.noEmail');
    expect(line.querySelector(`a[href="mailto:${OPERATOR.email}"]`)).not.toBeNull();
  });

  it('bleibt nach dem Absenden stehen (neutrale Bestätigung, es kommt womöglich nie eine Mail)', async () => {
    const fixture = await render();
    fixture.componentInstance.sent = true;
    fixture.detectChanges();
    const el: HTMLElement = fixture.nativeElement;
    expect(el.querySelector('input[name="email"]')).toBeNull();
    expect(el.querySelector('.no-email a[href^="mailto:"]')).not.toBeNull();
  });

  it('nimmt den Kontakt der Oberfläche (KidHub: eigene Adresse)', async () => {
    const el: HTMLElement = (await render({ contactEmail: 'kidhub@oberschm.id', imprint: false, kind: 'kidhub' })).nativeElement;
    expect(el.querySelector('.no-email a[href="mailto:kidhub@oberschm.id"]')).not.toBeNull();
  });
});

describe('resetMailSite (UX-031)', () => {
  const rookhub = { contactEmail: 'rookhub@oberschm.id', imprint: true };

  it('KidHub und LeagueHub sagen es über LEGAL_SITE — auch lokal', () => {
    expect(resetMailSite({ ...rookhub, kind: 'kidhub' }, 'localhost')).toBe('kidhub');
    expect(resetMailSite({ ...rookhub, kind: 'leaguehub' }, 'localhost')).toBe('leaguehub');
  });

  it('die Turnierseite wird am Host erkannt (Prod und Dev)', () => {
    expect(resetMailSite(rookhub, 'tournament.oberschmid.homes')).toBe('turnier');
    expect(resetMailSite(rookhub, 'turnier-dev.oberschmid.homes')).toBe('turnier');
  });

  it('RookHub, ClubHub, localhost: null — die API nimmt dann RookHub', () => {
    expect(resetMailSite(rookhub, 'rookhub.oberschmid.homes')).toBeNull();
    expect(resetMailSite(rookhub, 'clubhub.oberschmid.homes')).toBeNull();
    expect(resetMailSite(rookhub, 'localhost')).toBeNull();
    expect(resetMailSite({ ...rookhub, kind: 'rookhub' }, 'localhost')).toBeNull();
  });
});

describe('ForgotPasswordComponent — KidHub-Hinweis „dasselbe Konto“ (UX-031)', () => {
  async function render(legal?: object) {
    await TestBed.configureTestingModule({
      imports: [ForgotPasswordComponent],
      providers: [
        provideRouter([]),
        provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
        { provide: AuthService, useValue: {} },
        { provide: SnackbarService, useValue: {} },
        ...(legal ? [{ provide: LEGAL_SITE, useValue: legal }] : []),
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(ForgotPasswordComponent);
    fixture.detectChanges();
    return fixture;
  }

  it('KidHub: sagt, dass es dasselbe Konto wie bei RookHub ist — vor und nach dem Absenden', async () => {
    const fixture = await render({ contactEmail: 'kidhub@oberschm.id', imprint: false, kind: 'kidhub' });
    expect(fixture.nativeElement.querySelector('.kids-mail')?.textContent).toContain('auth.forgot.kidsSameAccount');
    fixture.componentInstance.sent = true;
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.kids-mail')).not.toBeNull();
  });

  it('RookHub und LeagueHub: kein KidHub-Hinweis', async () => {
    expect((await render()).nativeElement.querySelector('.kids-mail')).toBeNull();
  });
});

import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { TranslateLoader, TranslateService, TranslationObject, provideTranslateService } from '@ngx-translate/core';
import { Observable, Subject, isObservable, of } from 'rxjs';
import { adminGuard } from './admin.guard';
import { menuGuard } from './menu.guard';
import { AuthService } from './auth.service';
import { MenuService } from './menu.service';
import { SnackbarService } from './snackbar.service';

/** Sprachdatei, die erst auf Zuruf ankommt — wie `/i18n/de.json` (227 KB) beim ersten Besuch aus dem Netz. */
class WaitingLoader implements TranslateLoader {
  readonly files = new Map<string, Subject<TranslationObject>>();
  getTranslation(lang: string): Observable<TranslationObject> {
    if (!this.files.has(lang)) this.files.set(lang, new Subject());
    return this.files.get(lang)!;
  }
  arrive(lang: string, translations: TranslationObject): void {
    const file = this.files.get(lang)!;
    file.next(translations);
    file.complete();
  }
  fail(lang: string): void {
    this.files.get(lang)!.error(new Error('i18n nicht erreichbar'));
  }
}

/**
 * Kaltstart (UX-026-Nacharbeit): Deep-Link auf einen gesperrten Bereich in einem neuen Tab — die erste Navigation
 * läuft direkt nach `translate.use()` an, die Sprachdatei ist noch unterwegs. Echter TranslateService statt Attrappe:
 * mit `instant()` bekam der Snackbar hier den rohen Key „app.blocked“.
 */
describe('blockedNotice beim Kaltstart (Sprachdatei lädt noch)', () => {
  let loader: WaitingLoader;
  let snack: jasmine.Spy;

  beforeEach(() => {
    loader = new WaitingLoader();
    snack = jasmine.createSpy('info');
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideTranslateService({ loader: { provide: TranslateLoader, useValue: loader } }),
        { provide: AuthService, useValue: { isLoggedIn: true, isAdmin: false } },
        { provide: MenuService, useValue: { check: () => of(false), isVisible: (k: string) => k === 'dashboard' } },
        { provide: SnackbarService, useValue: { info: snack } },
      ],
    });
    TestBed.inject(TranslateService).use('de');
  });

  const de = { app: { blocked: 'Dieser Bereich ist für dein Konto nicht freigeschaltet.' } };

  it('adminGuard (synchron) meldet erst nach dem Laden — mit Übersetzung, nie mit dem Key', () => {
    TestBed.runInInjectionContext(() => adminGuard({} as any, {} as any));
    expect(snack).not.toHaveBeenCalled();

    loader.arrive('de', de);
    expect(snack).toHaveBeenCalledOnceWith(de.app.blocked);
    expect(snack).not.toHaveBeenCalledWith('app.blocked');
  });

  it('menuGuard: Menü-Antwort schneller als die Sprachdatei → trotzdem die Übersetzung', () => {
    const result = TestBed.runInInjectionContext(() => menuGuard('courses')({} as any, {} as any));
    (isObservable(result) ? result : of(result)).subscribe();
    expect(snack).not.toHaveBeenCalled();

    loader.arrive('de', de);
    expect(snack).toHaveBeenCalledOnceWith(de.app.blocked);
  });

  it('scheitert die Sprachdatei, kommt die Meldung trotzdem (Rückfall instant), statt stumm zu bleiben', () => {
    spyOn(console, 'warn');   // ngx-translate meldet die gescheiterte Sprachdatei selbst
    TestBed.runInInjectionContext(() => adminGuard({} as any, {} as any));
    loader.fail('de');
    expect(snack).toHaveBeenCalledTimes(1);
  });
});

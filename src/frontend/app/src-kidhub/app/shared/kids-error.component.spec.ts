import { TestBed } from '@angular/core/testing';
import { provideTranslateService } from '@ngx-translate/core';
import { KidsErrorComponent } from './kids-error.component';

/** Echte Texte einer Sprache (public/i18n). */
async function texts(lang: string): Promise<Record<string, unknown>> {
  for (const url of [`/i18n/${lang}.json`, `/base/i18n/${lang}.json`]) {
    const res = await fetch(url);
    if (res.ok) return res.json();
  }
  throw new Error(`${lang}.json nicht gefunden`);
}

describe('KidsErrorComponent', () => {
  it('„Nochmal" meldet sich beim Aufrufer', () => {
    TestBed.configureTestingModule({ imports: [KidsErrorComponent], providers: [provideTranslateService({ fallbackLang: 'en' })] });
    const f = TestBed.createComponent(KidsErrorComponent);
    let retried = 0;
    f.componentInstance.retry.subscribe(() => retried++);
    f.detectChanges();
    (f.nativeElement as HTMLElement).querySelector<HTMLButtonElement>('button.again')!.click();
    expect(retried).toBe(1);
  });

  /** Codereview 2026-09-29, UX-062: der Satz sagte „Bitte später noch einmal versuchen", der Knopf daneben „Nochmal" —
   *  der Text sagte „später", der Knopf „jetzt". */
  for (const [lang, later] of [['de', /später/i], ['en', /later/i], ['hr', /kasnije/i], ['hu', /később/i]] as const) {
    it(`${lang}: der Fehlersatz schickt nicht auf „später", der Knopf heisst „Nochmal"`, async () => {
      const kids = (await texts(lang))['kids'] as Record<string, string>;
      expect(kids['loadError']).toBeTruthy();
      expect(kids['loadError']).not.toMatch(later);
      expect(kids['retry']).toBeTruthy();
    });
  }
});

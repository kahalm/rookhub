import { TestBed } from '@angular/core/testing';
import { TranslateService, TranslationObject } from '@ngx-translate/core';

const cache = new Map<string, Promise<TranslationObject>>();

/** Die ausgelieferte Sprachdatei (`public/i18n/<lang>.json`, Karma unter `/i18n/` bzw. `/base/i18n/`), je Lauf einmal geholt. */
export function loadTranslations(lang: string): Promise<TranslationObject> {
  let p = cache.get(lang);
  if (!p) {
    p = (async () => {
      for (const url of [`/i18n/${lang}.json`, `/base/i18n/${lang}.json`]) {
        const res = await fetch(url);
        if (res.ok) return res.json();
      }
      throw new Error(`${lang}.json nicht gefunden`);
    })();
    cache.set(lang, p);
  }
  return p;
}

/**
 * Specs mit den ECHTEN Texten einer Sprache (Spielerkarte, e1-rk-prep-card-german): lädt die Sprachdatei in den
 * TranslateService des TestBeds und stellt die Sprache ein — wie LeagueHub (`de`) bzw. RookHub (Oberflächensprache).
 */
export async function useRealTranslations(lang: string): Promise<TranslateService> {
  const translate = TestBed.inject(TranslateService);
  translate.setTranslation(lang, await loadTranslations(lang));
  translate.use(lang);
  return translate;
}

/** Wie {@link useRealTranslations}, aber synchron mit einer vorab (`beforeAll`) geholten Sprachdatei — für `fakeAsync`. */
export function applyTranslations(lang: string, json: TranslationObject): TranslateService {
  const translate = TestBed.inject(TranslateService);
  translate.setTranslation(lang, json);
  translate.use(lang);
  return translate;
}

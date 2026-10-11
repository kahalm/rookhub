import { TestBed } from '@angular/core/testing';
import { MatPaginatorIntl } from '@angular/material/paginator';
import { TranslateService, provideTranslateService } from '@ngx-translate/core';
import { TranslatedPaginatorIntl } from './translated-paginator-intl';
import { appConfig } from '../app.config';

// UI-Sweep 2026-10-11 (r2-rk-paginator-en): die Blätterleiste stand englisch da („Items per page:", „1 – 11 of 11").
describe('TranslatedPaginatorIntl', () => {
  function setup(): { intl: TranslatedPaginatorIntl; translate: TranslateService } {
    TestBed.configureTestingModule({
      providers: [provideTranslateService({ fallbackLang: 'en' }), { provide: MatPaginatorIntl, useClass: TranslatedPaginatorIntl }],
    });
    const translate = TestBed.inject(TranslateService);
    translate.setTranslation('de', { common: { paginator: {
      itemsPerPage: 'Einträge pro Seite:', firstPage: 'Erste Seite', previousPage: 'Vorherige Seite',
      nextPage: 'Nächste Seite', lastPage: 'Letzte Seite',
      range: '{{start}}–{{end}} von {{length}}', rangeEmpty: '0 von {{length}}',
    } } });
    translate.setTranslation('en', { common: { paginator: {
      itemsPerPage: 'Items per page:', firstPage: 'First page', previousPage: 'Previous page',
      nextPage: 'Next page', lastPage: 'Last page',
      range: '{{start}}–{{end}} of {{length}}', rangeEmpty: '0 of {{length}}',
    } } });
    return { intl: TestBed.inject(MatPaginatorIntl) as TranslatedPaginatorIntl, translate };
  }

  it('labels and range follow the active language and announce every change', () => {
    const { intl, translate } = setup();
    let changes = 0;
    intl.changes.subscribe(() => changes++);
    translate.use('de');
    expect(intl.itemsPerPageLabel).toBe('Einträge pro Seite:');
    expect(intl.nextPageLabel).toBe('Nächste Seite');
    expect(intl.getRangeLabel(0, 20, 11)).toBe('1–11 von 11');
    expect(intl.getRangeLabel(1, 10, 25)).toBe('11–20 von 25');
    expect(intl.getRangeLabel(0, 10, 0)).toBe('0 von 0');
    const before = changes;
    translate.use('en');
    expect(changes).toBeGreaterThan(before);
    expect(intl.itemsPerPageLabel).toBe('Items per page:');
    expect(intl.getRangeLabel(2, 10, 25)).toBe('21–25 of 25');
  });

  it('is provided app-wide', () => {
    const provided = (appConfig.providers as unknown[]).some(p =>
      !!p && typeof p === 'object' && (p as { provide?: unknown }).provide === MatPaginatorIntl
      && (p as { useClass?: unknown }).useClass === TranslatedPaginatorIntl);
    expect(provided).toBeTrue();
  });
});

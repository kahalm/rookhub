import { TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { of } from 'rxjs';
import { SnackbarService } from '../../core/snackbar.service';
import { EbookService, EbookTranslationStatus } from './ebook.service';
import { EbookTranslationDialogComponent } from './ebook-translation-dialog.component';

const STATUS: EbookTranslationStatus = {
  enabled: true, modelAvailable: true, languages: ['en'], quietNow: false, quietUntil: null,
  running: { ebookId: 3, title: 'Mi sistema', language: 'en', sourceLanguage: 'es', segmentsDone: 40, segmentsTotal: 160, startedAt: '', etaMinutes: 12 },
  bookLanguages: [{ language: 'de', books: 300 }, { language: 'und', books: 4 }],
  targets: [{ language: 'en', done: 5, pending: 1, failed: 1, noText: 2, remaining: 310, remainingFirstRound: 10 }],
  failed: [{ ebookId: 8, title: 'Kaputt', language: 'en', sourceLanguage: 'fr', state: 'failed', priority: false, segmentsDone: 0, segmentsTotal: 0, segmentsFailed: 0, error: 'The EPUB is not readable', completedAt: null }],
  next: [],
};

describe('EbookTranslationDialogComponent', () => {
  let service: jasmine.SpyObj<EbookService>;

  beforeEach(async () => {
    service = jasmine.createSpyObj<EbookService>('EbookService', ['translationStatus', 'retryFailedTranslations']);
    service.translationStatus.and.returnValue(of(STATUS));
    service.retryFailedTranslations.and.returnValue(of({ queued: 1 }));
    await TestBed.configureTestingModule({
      imports: [EbookTranslationDialogComponent],
      providers: [provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }),
        { provide: EbookService, useValue: service },
        { provide: SnackbarService, useValue: { warn: () => {}, quick: () => {} } }],
    }).compileComponents();
  });

  it('zeigt laufendes Buch, Zähler und Gescheiterte; „wiederholen“ reiht neu ein und lädt neu', () => {
    const fixture = TestBed.createComponent(EbookTranslationDialogComponent);
    fixture.detectChanges();
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Mi sistema');
    expect(text).toContain('Kaputt');
    expect(fixture.nativeElement.querySelector('mat-progress-bar')).not.toBeNull();
    fixture.componentInstance.retryFailed();
    expect(service.retryFailedTranslations).toHaveBeenCalled();
    expect(service.translationStatus).toHaveBeenCalledTimes(2);
  });

  it('Sprachnamen: Sonderwerte übersetzt, ISO-Code als Name', () => {
    const c = TestBed.createComponent(EbookTranslationDialogComponent).componentInstance;
    expect(c.languageLabel('und')).toBe('ebooks.translation.unclear');
    expect(c.languageLabel('notext')).toBe('ebooks.translation.noTextLabel');
    expect(c.languageLabel('es')).not.toBe('es');
  });
});

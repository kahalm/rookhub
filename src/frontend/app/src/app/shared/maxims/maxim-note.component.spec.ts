import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { TranslateService, provideTranslateService } from '@ngx-translate/core';
import { firstValueFrom } from 'rxjs';
import { MaximNoteComponent } from './maxim-note.component';
import { ChessMaxim, localizeMaxim, splitMaxim } from './chess-maxims.service';

const LIST: ChessMaxim[] = [{
  id: 'raum-und-tausch', text: 'Wer Raumvorteil hat, vermeidet Figurentausch – wer beengt steht, befreit sich durch Tausch.',
  category: 'Figuren', when: '…', textEn: 'With more space, avoid exchanges.', categoryEn: 'Pieces', whenEn: 'Space.', sources: [
    { ebookId: 338, author: 'Ludek Pachman', title: 'Complete Chess Strategy', location: 'PDF-S. 41', page: 41, quote: 'White will not want to exchange pieces', accessible: true },
    { ebookId: 1314, author: 'Jeremy Silman', title: 'Schach mit neuem Schwung', location: 'Teil 3; E-Book ohne Seitenzahlen, bei ca. 37 %', percent: 37, accessible: true },
    { ebookId: 999, author: 'Geheim', title: 'Nicht freigegeben', location: 'PDF-S. 3', page: 3, accessible: false },
  ],
}];

describe('splitMaxim (0.917.0)', () => {
  it('trennt die Merksatz-Zeile ab', () => {
    expect(splitMaxim('Absatz.\n\nMerksatz: Türme gehören auf offene Linien.'))
      .toEqual({ body: 'Absatz.', maxim: 'Türme gehören auf offene Linien.' });
    expect(splitMaxim('Nur Text.')).toEqual({ body: 'Nur Text.', maxim: null });
  });
});

describe('localizeMaxim', () => {
  const m = { id: 'a', text: 'Satz', category: 'Kat', when: 'Wann', sources: [], textEn: 'Line', categoryEn: 'Cat', whenEn: 'When' };

  it('Deutsch nur bei de, sonst Englisch', () => {
    expect(localizeMaxim(m, 'de').text).toBe('Satz');
    expect(localizeMaxim(m, 'de-AT').when).toBe('Wann');
    for (const lang of ['en', 'hr', 'fr', 'ja', null, undefined]) {
      expect(localizeMaxim(m, lang)).toEqual(jasmine.objectContaining({ text: 'Line', category: 'Cat', when: 'When' }));
    }
  });

  it('ohne englische Felder bleibt der deutsche Text', () => {
    expect(localizeMaxim({ ...m, textEn: null, categoryEn: '', whenEn: undefined }, 'en'))
      .toEqual(jasmine.objectContaining({ text: 'Satz', category: 'Kat', when: 'Wann' }));
  });

  it('die Quellen bleiben unverändert', () => {
    expect(localizeMaxim(LIST[0], 'en').sources).toBe(LIST[0].sources);
  });
});

describe('MaximNoteComponent (0.917.0)', () => {
  function make(text: string) {
    TestBed.configureTestingModule({
      imports: [MaximNoteComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]), provideTranslateService({ fallbackLang: 'en' })],
    });
    const f = TestBed.createComponent(MaximNoteComponent);
    f.componentRef.setInput('text', text);
    f.detectChanges();
    TestBed.inject(HttpTestingController).expectOne('/api/maxims').flush(LIST);
    f.detectChanges();
    return { f, el: f.nativeElement as HTMLElement };
  }

  it('zeigt die gespeicherte deutsche Zeile in der Oberflächensprache und wechselt zur Laufzeit', async () => {
    const { f, el } = make(LIST[0].text);
    const translate = TestBed.inject(TranslateService);
    await firstValueFrom(translate.use('en'));
    f.detectChanges();
    expect(el.querySelector('.maxim')!.textContent).toContain('With more space, avoid exchanges.');
    await firstValueFrom(translate.use('de'));
    f.detectChanges();
    expect(el.querySelector('.maxim')!.textContent).toContain(LIST[0].text);
    await firstValueFrom(translate.use('hr'));
    f.detectChanges();
    expect(el.querySelector('.maxim')!.textContent).toContain('With more space, avoid exchanges.');
  });

  it('(i) klappt die Buchstellen auf — PDF mit Seite im Link, EPUB mit Stelle', () => {
    const { f, el } = make(LIST[0].text);
    expect(el.querySelector('.sources')).toBeNull();
    (el.querySelector('button.info') as HTMLButtonElement).click();
    f.detectChanges();
    const links = Array.from(el.querySelectorAll('.sources a')) as HTMLAnchorElement[];
    expect(links.map(a => a.getAttribute('href'))).toEqual(['/ebooks/338?page=41&fmt=pdf', '/ebooks/1314?pct=37', '/maxims?m=raum-und-tausch']);
    // Buch ohne Zugriff: Titel ja, Link nein.
    expect(el.textContent).toContain('Nicht freigegeben');
    expect(el.textContent).toContain('White will not want to exchange pieces');
    expect(el.textContent).toContain('Teil 3 ~37 %');
  });

  it('ein Satz, der nicht in der Liste steht (frei erfunden, vor 0.917.0): wird nicht gezeigt', () => {
    const { el } = make('Erst denken, dann ziehen.');
    expect(el.textContent).not.toContain('Erst denken');
    expect(el.querySelector('.maxim')).toBeNull();
  });
});

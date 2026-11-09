import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { MaximNoteComponent } from './maxim-note.component';
import { ChessMaxim, splitMaxim } from './chess-maxims.service';

const LIST: ChessMaxim[] = [{
  id: 'raum-und-tausch', text: 'Wer Raumvorteil hat, vermeidet Figurentausch – wer beengt steht, befreit sich durch Tausch.',
  category: 'Figuren', when: '…', sources: [
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

  it('(i) klappt die Buchstellen auf — PDF mit Seite im Link, EPUB mit Stelle', () => {
    const { f, el } = make(LIST[0].text);
    expect(el.querySelector('.sources')).toBeNull();
    (el.querySelector('button.info') as HTMLButtonElement).click();
    f.detectChanges();
    const links = Array.from(el.querySelectorAll('.sources a')) as HTMLAnchorElement[];
    expect(links.map(a => a.getAttribute('href'))).toEqual(['/ebooks/338?page=41', '/ebooks/1314', '/maxims?m=raum-und-tausch']);
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

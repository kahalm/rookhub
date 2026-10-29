import { TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { TranslateService, provideTranslateService } from '@ngx-translate/core';
import { firstValueFrom } from 'rxjs';
import { EBOOK_TAGS_COLLAPSED, EbookFilterPanelComponent, ebookLanguageLabel } from './ebook-filter-panel.component';
import { EbookLanguageFacet, EbookTagFacet } from './ebook.service';

describe('EbookFilterPanelComponent', () => {
  async function render(inputs: { tags?: EbookTagFacet[]; languages?: EbookLanguageFacet[]; translations?: EbookLanguageFacet[]; tag?: string | null }, lang = 'en') {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      imports: [EbookFilterPanelComponent],
      providers: [provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' })],
    });
    const translate = TestBed.inject(TranslateService);
    translate.setTranslation('en', { ebooks: {
      tags: { opening: 'Opening', endgame: 'Endgame', tactics: 'Tactics', strategy: 'Strategy' },
      filter: { languageUnknown: 'Unknown', translatedInto: 'Translation: {{lang}}' },
    } });
    translate.setTranslation('de', { ebooks: {
      tags: { opening: 'Eröffnung', endgame: 'Endspiel', tactics: 'Taktik', strategy: 'Strategie' },
      filter: { languageUnknown: 'Unbekannt', translatedInto: 'Übersetzung: {{lang}}' },
    } });
    await firstValueFrom(translate.use(lang));
    const fixture = TestBed.createComponent(EbookFilterPanelComponent);
    fixture.componentRef.setInput('tags', inputs.tags ?? []);
    fixture.componentRef.setInput('languages', inputs.languages ?? []);
    fixture.componentRef.setInput('translations', inputs.translations ?? []);
    fixture.componentRef.setInput('tag', inputs.tag ?? null);
    fixture.detectChanges();
    return fixture;
  }

  const chipTexts = (el: HTMLElement, sel: string) =>
    Array.from(el.querySelectorAll(sel)).map(b => (b.firstChild?.textContent ?? '').trim());

  it('zeigt die übersetzte Beschriftung, meldet aber das gespeicherte Schlagwort', async () => {
    const fixture = await render({ tags: [{ tag: 'Eröffnung', count: 5 }, { tag: 'Top 100', count: 2 }] });
    const el: HTMLElement = fixture.nativeElement;
    expect(chipTexts(el, '.eb-tags .eb-chip:not(.eb-tags-more)')).toEqual(['Opening', 'Top 100']);
    const picked: string[] = [];
    fixture.componentInstance.tagChange.subscribe(t => picked.push(t));
    el.querySelector<HTMLButtonElement>('[data-tag="Eröffnung"]')!.click();
    expect(picked).toEqual(['Eröffnung']);
  });

  it('c1-eb-tag-filter-lead: zugeklappt die häufigsten + das aktive, aufgeklappt alle nach Beschriftung sortiert', async () => {
    const many = Array.from({ length: 20 }, (_, i) => ({ tag: `T${String(i).padStart(2, '0')}`, count: 40 - i }));
    const fixture = await render({ tags: many, tag: 'T15' });
    const c = fixture.componentInstance;
    expect(c.visibleTags().map(t => t.tag)).toEqual([...many.slice(0, EBOOK_TAGS_COLLAPSED).map(t => t.tag), 'T15']);
    const el: HTMLElement = fixture.nativeElement;
    const more = el.querySelector<HTMLButtonElement>('.eb-tags-more')!;
    expect(more.getAttribute('aria-expanded')).toBe('false');
    more.click();
    fixture.detectChanges();
    expect(el.querySelectorAll('.eb-tags .eb-chip:not(.eb-tags-more)').length).toBe(20);
    // Wenige Schlagwörter: alle, ohne Umschalter.
    const few = await render({ tags: many.slice(0, 5) });
    expect(few.componentInstance.visibleTags().length).toBe(5);
    expect((few.nativeElement as HTMLElement).querySelector('.eb-tags-more')).toBeNull();
  });

  it('aufgeklappt folgt die Reihenfolge der angezeigten Sprache', async () => {
    const tags = [{ tag: 'Taktik', count: 9 }, { tag: 'Eröffnung', count: 8 }, { tag: 'Endspiel', count: 7 }, { tag: 'Strategie', count: 6 }];
    const en = await render({ tags });
    en.componentInstance.tagsOpen.set(true);
    expect(en.componentInstance.visibleTags().map(t => t.tag)).toEqual(['Endspiel', 'Eröffnung', 'Strategie', 'Taktik']);   // Endgame, Opening, Strategy, Tactics
    const de = await render({ tags }, 'de');
    de.componentInstance.tagsOpen.set(true);
    expect(de.componentInstance.visibleTags().map(t => t.tag)).toEqual(['Endspiel', 'Eröffnung', 'Strategie', 'Taktik']);
    const reordered = [{ tag: 'Taktik', count: 9 }, { tag: 'Eröffnung', count: 8 }];
    const enTwo = await render({ tags: reordered });
    enTwo.componentInstance.tagsOpen.set(true);
    expect(enTwo.componentInstance.visibleTags().map(t => t.tag)).toEqual(['Eröffnung', 'Taktik']);   // Opening < Tactics
  });

  it('Sprachen in der Oberflächensprache, unbestimmte als „Unknown“, Übersetzungen als eigener Wahlpunkt', async () => {
    const fixture = await render({
      languages: [{ language: 'de', count: 3 }, { language: null, count: 1 }],
      translations: [{ language: 'en', count: 2 }],
    });
    const el: HTMLElement = fixture.nativeElement;
    expect(chipTexts(el, '.eb-langs [data-lang]')).toEqual(['German', 'Unknown']);
    expect(el.querySelector('[data-translation="en"]')!.textContent).toContain('Translation: English');
    const langs: string[] = [];
    const trs: string[] = [];
    fixture.componentInstance.languageChange.subscribe(v => langs.push(v));
    fixture.componentInstance.translationChange.subscribe(v => trs.push(v));
    el.querySelector<HTMLButtonElement>('[data-lang="unknown"]')!.click();
    el.querySelector<HTMLButtonElement>('[data-translation="en"]')!.click();
    expect(langs).toEqual(['unknown']);
    expect(trs).toEqual(['en']);
    expect(ebookLanguageLabel('hu', TestBed.inject(TranslateService), 'de')).toBe('Ungarisch');
  });

  it('390 px: viele lange Schlagwörter brechen um, kein waagerechtes Scrollen', async () => {
    const tags = Array.from({ length: 40 }, (_, i) => ({ tag: `Ein ziemlich langes Schlagwort Nummer ${i}`, count: i }));
    const fixture = await render({ tags, languages: [{ language: 'de', count: 3 }], translations: [{ language: 'en', count: 1 }] });
    fixture.componentInstance.tagsOpen.set(true);
    fixture.detectChanges();
    const host: HTMLElement = fixture.nativeElement;
    host.style.display = 'flex';
    host.style.width = '390px';
    host.style.boxSizing = 'border-box';
    document.body.appendChild(host);
    expect(host.scrollWidth).toBeLessThanOrEqual(host.clientWidth);
    host.remove();
  });
});

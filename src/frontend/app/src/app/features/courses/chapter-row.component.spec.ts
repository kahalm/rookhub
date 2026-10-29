import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { ChapterRowComponent } from './chapter-row.component';
import { CourseLine, CourseManageChapter } from './course.types';

function chapter(over: Partial<CourseManageChapter> = {}): CourseManageChapter {
  return {
    name: 'Pins', lineCount: 3, quizCount: 2, solvedCount: 1, progressPercent: 50,
    solverIndex: 0, firstLineId: 11, ...over,
  } as CourseManageChapter;
}

function line(over: Partial<CourseLine> = {}): CourseLine {
  return { id: 11, round: '1', title: null, fen: '8/8/8/8/8/8/8/K6k w - - 0 1', comment: 'c', moveCount: 2, ...over } as CourseLine;
}

describe('ChapterRowComponent', () => {
  async function render(inputs: Record<string, unknown> = {}) {
    await TestBed.configureTestingModule({
      imports: [ChapterRowComponent],
      providers: [provideRouter([]), provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' })],
    }).compileComponents();
    const fixture = TestBed.createComponent(ChapterRowComponent);
    fixture.componentRef.setInput('chapter', chapter());
    fixture.componentRef.setInput('bookId', 58);
    fixture.componentRef.setInput('label', 'Pins');
    for (const [k, v] of Object.entries(inputs)) fixture.componentRef.setInput(k, v);
    fixture.detectChanges();
    return { fixture, el: fixture.nativeElement as HTMLElement };
  }

  it('zeigt Name, Zähler und Balken und meldet den Aufklapper als Ereignis', async () => {
    const { fixture, el } = await render();
    expect(el.querySelector('.chapter-name')!.textContent!.trim()).toBe('Pins');
    expect(el.querySelector('.chapter-label')!.textContent!.trim()).toBe('1/2');
    let toggled = 0;
    fixture.componentInstance.expandToggle.subscribe(() => toggled++);
    (el.querySelector('button.expander') as HTMLButtonElement).click();
    expect(toggled).toBe(1);
    expect(el.querySelector('button.expander')!.getAttribute('aria-expanded')).toBe('false');
  });

  it('aufgeklappt: Linien als Tabelle, Löschen nur für Verwalter', async () => {
    const { fixture, el } = await render({ expanded: true, lines: [line(), line({ id: 12, moveCount: 0 })] });
    expect(el.querySelectorAll('.line-table tr').length).toBe(2);
    expect(el.querySelector('.line-table .row-actions .danger')).toBeNull();
    fixture.componentRef.setInput('canManage', true);
    fixture.detectChanges();
    const deleted: number[] = [];
    fixture.componentInstance.deleteLine.subscribe(l => deleted.push(l.id));
    (el.querySelector('.line-table .row-actions .danger') as HTMLButtonElement).click();
    expect(deleted).toEqual([11]);
  });

  it('Umbenennen: Feld mit zweiseitigem Entwurf, Enter speichert', async () => {
    const { fixture, el } = await render({ renaming: true, renameDraft: 'Alt' });
    await fixture.whenStable();
    const input = el.querySelector('input.rename-input') as HTMLInputElement;
    expect(input.value).toBe('Alt');
    input.value = 'Neu';
    input.dispatchEvent(new Event('input'));
    expect(fixture.componentInstance.renameDraft()).toBe('Neu');
    let committed = 0;
    fixture.componentInstance.commitRename.subscribe(() => committed++);
    input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));
    expect(committed).toBe(1);
  });

  it('Kurs ohne Aufgaben: kein Kapitelbalken', async () => {
    const { el } = await render({ isEmptyQuiz: true });
    expect(el.querySelector('.chapter-progress')).toBeNull();
  });

  it('Kapitelzähler in fester, rechtsbündiger Breite (b1-rk-kapitel-balken-versatz)', () => {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const css = ((ChapterRowComponent as any).ɵcmp.styles as string[]).join('\n').replace(/\s+/g, ' ');
    expect(css).toMatch(/\.chapter-label[^{]*\{[^}]*min-width: 7ch;[^}]*text-align: right/);
  });

  // d8-rk-kapitel-menue-spalte-versetzt: am Handy stand im Studienbuch der Linienzähler HINTER dem ⋮ und schob es je nach
  // Textlänge anders weit — ohne sichtbaren Balken steht der Zähler jetzt vor dem ⋮ (order 1) in fester Breite rechtsbündig.
  // Karma ist breit → Regel als CSS-Text, ihre Bedingung gegen die echten Zeilen (Studienbuch / Kurs mit Aufgaben).
  it('d8: ohne Balken steht der Linienzähler am Handy vor dem ⋮, fest breit und rechtsbündig', async () => {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const css = ((ChapterRowComponent as any).ɵcmp.styles as string[]).join('\n').replace(/\s+/g, ' ');
    const at = css.indexOf('@media (max-width: 599.98px)');
    const block = css.slice(at);
    expect(block).toMatch(/:not\(:has\(> ?\.chapter-progress[^)]*:not\(\.chapter-progress--none\)\)\)[^{]*> ?\.chapter-counts[^{]*\{[^}]*order: 1;[^}]*min-width: 5\.5rem;[^}]*text-align: right/);
    const rule = '.chapter-row:not(:has(> .chapter-progress:not(.chapter-progress--none))) > .chapter-counts';

    const study = await render({ chapter: chapter({ quizCount: 0, solvedCount: 0, progressPercent: 0, lineCount: 6 }) });
    expect(study.el.querySelector('.chapter-progress--none')).not.toBeNull();
    expect(study.el.querySelector('.chapter-counts')!.matches(rule)).withContext('Studienbuch').toBeTrue();
    TestBed.resetTestingModule();
    const empty = await render({ isEmptyQuiz: true });
    expect(empty.el.querySelector('.chapter-counts')!.matches(rule)).withContext('Kurs ohne Aufgaben').toBeTrue();
    TestBed.resetTestingModule();
    const quiz = await render();
    expect(quiz.el.querySelector('.chapter-counts')!.matches(rule)).withContext('mit Balken: Zeile 2 wie bisher').toBeFalse();
  });
});

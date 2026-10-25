import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { CalcStatusPanelComponent } from './calc-status-panel.component';

describe('CalcStatusPanelComponent', () => {
  async function render(inputs: Record<string, unknown>) {
    await TestBed.configureTestingModule({
      imports: [CalcStatusPanelComponent],
      providers: [provideRouter([]), provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' })],
    }).compileComponents();
    const fixture = TestBed.createComponent(CalcStatusPanelComponent);
    for (const [k, v] of Object.entries(inputs)) fixture.componentRef.setInput(k, v);
    fixture.detectChanges();
    return { c: fixture.componentInstance, el: fixture.nativeElement as HTMLElement };
  }

  it('mitten im Kapitel: nur die Fußzeile', async () => {
    const { el } = await render({ doneCount: 2, positionCount: 6 });
    expect(el.querySelector('.calc-chapter-done')).toBeNull();
    expect(el.querySelector('.calc-foot .calc-hint')!.textContent).toContain('calc.notSavedYet');
    expect(el.querySelector('.calc-local-note')).toBeNull();
  });

  it('am Ende angekommen: „durch" plus Weg ins nächste Kapitel; nur eingestiegen: nur der Weg', async () => {
    const done = await render({ atChapterEnd: true, atLastPosition: true, hasNextChapter: true });
    expect(done.el.querySelector('.calc-chapter-done--onward')).toBeNull();
    expect(done.el.querySelector('.calc-chapter-done-text')).not.toBeNull();
    let next = 0;
    done.c.nextChapter.subscribe(() => next++);
    (done.el.querySelector('.calc-next-chapter') as HTMLButtonElement).click();
    expect(next).toBe(1);
    TestBed.resetTestingModule();
    const onward = await render({ atChapterEnd: false, atLastPosition: true, hasNextChapter: true });
    expect(onward.el.querySelector('.calc-chapter-done--onward')).not.toBeNull();
    expect(onward.el.querySelector('.calc-chapter-done-text')).toBeNull();
  });

  it('Geräte-Hinweis und Warnung mit Anmelde-Link und Wegklicken', async () => {
    const { c, el } = await render({ showLocalNotice: true, showLocalWarning: true, loginReturnUrl: '/c?pos=7' });
    const notes = el.querySelectorAll('.calc-local-note');
    expect(notes.length).toBe(2);
    expect(notes[1].classList).toContain('calc-local-note--failed');
    expect((notes[0].querySelector('a') as HTMLAnchorElement).getAttribute('href')).toContain('returnUrl=%2Fc%3Fpos%3D7');
    const log: string[] = [];
    c.dismissNotice.subscribe(() => log.push('notice'));
    c.dismissWarning.subscribe(() => log.push('warning'));
    (notes[0].querySelector('.calc-note-close') as HTMLButtonElement).click();
    (notes[1].querySelector('.calc-note-close') as HTMLButtonElement).click();
    expect(log).toEqual(['notice', 'warning']);
  });
});

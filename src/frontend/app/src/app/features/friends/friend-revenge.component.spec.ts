import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { provideTranslateService, TranslateService } from '@ngx-translate/core';
import { of } from 'rxjs';
import { FriendRevengeComponent, RevengeList } from './friend-revenge.component';
import { FriendsService } from '../../core/friends.service';
import { SnackbarService } from '../../core/snackbar.service';
import { puzzleThemeList } from '../puzzles/puzzle-theme.pipe';

describe('puzzleThemeList', () => {
  it('returns an empty list for null/empty input', () => {
    expect(puzzleThemeList(null)).toEqual([]);
    expect(puzzleThemeList('')).toEqual([]);
  });

  it('splits space-separated themes', () => {
    expect(puzzleThemeList('fork pin')).toEqual(['fork', 'pin']);
  });

  it('caps the number of themes at the limit', () => {
    expect(puzzleThemeList('a b c d e f', 4)).toEqual(['a', 'b', 'c', 'd']);
  });

  it('ignores extra whitespace', () => {
    expect(puzzleThemeList('fork   pin')).toEqual(['fork', 'pin']);
  });
});

describe('FriendRevengeComponent (UI-Sweep 2026-10-11)', () => {
  const data: RevengeList = {
    userId: 7, username: 'bob', displayName: null,
    puzzles: [
      { puzzleId: 1, lichessId: 'a', rating: 1545, themes: 'kingsideAttack mateIn1 oneMove', failCount: 1,
        lastFailedAt: '2026-10-01T00:00:00Z', solvedByViewer: false },
      { puzzleId: 2, lichessId: 'b', rating: 905, themes: 'mateIn1', failCount: 2,
        lastFailedAt: '2026-10-01T00:00:00Z', solvedByViewer: true },
    ],
  };

  function render(list: RevengeList = data): HTMLElement {
    TestBed.configureTestingModule({
      imports: [FriendRevengeComponent],
      providers: [
        provideRouter([]),
        provideTranslateService({ fallbackLang: 'en' }),
        { provide: FriendsService, useValue: { getRevenge: () => of(list) } },
        { provide: SnackbarService, useValue: { info: () => {} } },
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ userId: '7' }) } } },
      ],
    });
    const translate = TestBed.inject(TranslateService);
    translate.setTranslation('de', {
      puzzleThemes: { kingsideAttack: 'Königsangriff', mateIn1: 'Matt in 1', oneMove: 'Einzüger' },
      friends: { revenge: { solve: 'Lösen' } },
    });
    translate.use('de');
    const fixture = TestBed.createComponent(FriendRevengeComponent);
    fixture.detectChanges();
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('zeigt die Themen übersetzt statt als rohe Schlüssel (d1-rk-revenge-themen-rohschluessel)', () => {
    const el = render();
    const lines = Array.from(el.querySelectorAll('.themes')).map(e => e.textContent ?? '');
    expect(lines.length).toBe(2);
    expect(lines[0]).toContain('Königsangriff');
    expect(lines[0]).toContain('Matt in 1');
    expect(lines[0]).toContain('Einzüger');
    lines.forEach(l => {
      expect(l).not.toContain('kingsideAttack');
      expect(l).not.toContain('mateIn1');
    });
    expect(el.querySelectorAll('.themes')[0].querySelectorAll('.theme').length).toBe(3);
  });

  it('„Lösen" liegt in einem Container mit matListItemMeta, nicht selbst im Listen-Endbereich (d1-rk-revenge-loesen-knopf-kaputt)', () => {
    const el = render();
    const buttons = Array.from(el.querySelectorAll('button[mat-flat-button]'));
    expect(buttons.length).toBe(1);
    const btn = buttons[0];
    expect(btn.textContent).toContain('Lösen');
    expect(btn.classList.contains('mdc-list-item__end')).toBeFalse();
    expect(btn.classList.contains('mat-mdc-list-item-meta')).toBeFalse();
    expect(btn.parentElement?.classList.contains('rev-action')).toBeTrue();
    // Symbolknopf der erledigten Liste ebenfalls im Container.
    el.querySelectorAll('button[mat-icon-button]').forEach(b =>
      expect(b.classList.contains('mdc-list-item__end')).toBeFalse());
  });
  it('bricht die Themenzeile um statt sie mit „…“ abzuschneiden (z1-rk-revenge-themen-umbruch)', () => {
    const el = render();
    document.body.appendChild(el);
    const line = el.querySelector('.themes') as HTMLElement;
    const cs = getComputedStyle(line);
    expect(cs.whiteSpace).toBe('normal');
    expect(cs.textOverflow).toBe('clip');
    expect(cs.overflow).toBe('visible');
    el.remove();
  });

  // d5-rk-revanche-leerzustand-text
  it('Auftrag „knack sie“ nur bei offenen Puzzles; ohne offene erklärt der Leerzustand allein', () => {
    expect(render().querySelector('.hint')).not.toBeNull();
    TestBed.resetTestingModule();
    const none = render({ ...data, puzzles: data.puzzles.filter(p => p.solvedByViewer) });
    expect(none.querySelector('.hint')).toBeNull();
    expect(none.querySelector('.empty-state')).not.toBeNull();
  });
});

/** Sprachdatei wie im Browser laden (Karma liefert public/ unter / oder /base/). */
async function loadLang(lang: string): Promise<any> {
  for (const url of [`/i18n/${lang}.json`, `/base/i18n/${lang}.json`]) {
    const res = await fetch(url).catch(() => null);
    if (res?.ok) return res.json();
  }
  throw new Error('i18n ' + lang + ' nicht gefunden');
}

// d5-rk-revanche-leerzustand-text: eindeutiger Satz ohne „sie … sie“.
describe('friends.revenge.hint (d5)', () => {
  it('deutsch: „Puzzles, die {{name}} noch nicht lösen konnte …“', async () => {
    expect((await loadLang('de')).friends.revenge.hint)
      .toBe('Puzzles, die {{name}} noch nicht lösen konnte — knack sie und mach es besser!');
  });
});

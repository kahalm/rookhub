import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { TranslateService, provideTranslateService } from '@ngx-translate/core';
import { AuthService } from '../../../core/auth.service';
import { ExternalEngineService } from '../../analysis/external-engine.service';
import { GameEvals } from '../game-review.util';
import { SheetEditSession } from '../sheet-edit-session';
import { ScanPhotos } from './scan-photos';
import { SheetWorkbenchComponent } from './sheet-workbench.component';

/** Wie eine Seite die Werkbank einbindet (RookHub `/games/:id/edit`, LeagueHub Formular prüfen). */
@Component({
  standalone: true,
  imports: [SheetWorkbenchComponent],
  template: `
    <app-sheet-workbench [session]="s" [photos]="ph" [evals]="evals()" [savedSans]="saved">
      <button sheetPhotoTools class="tool">Werkzeug</button>
      <p sheetPlyExtra class="ply-extra">Kommentar</p>
      <p sheetEnd class="end-extra">Weiter</p>
    </app-sheet-workbench>`,
})
class HostComponent {
  readonly s = new SheetEditSession({ resolve: () => of({ plies: [], unresolved: [], unresolvedFrom: null }) });
  readonly ph = new ScanPhotos({ fetchView: () => Promise.resolve(new Blob(['v'])), setPageSize: () => {} });
  readonly evals = signal<GameEvals | null>(null);
  readonly saved = ['e4', 'e5', 'Nf3'];
}

describe('SheetWorkbenchComponent', () => {
  function setup() {
    TestBed.configureTestingModule({
      imports: [HostComponent],
      providers: [
        provideTranslateService({ fallbackLang: 'en' }),
        { provide: AuthService, useValue: { isLoggedIn: false } },
        { provide: ExternalEngineService, useValue: { listEngines: () => of({ engines: [] }), analyse: () => of() } },
      ],
    });
    TestBed.inject(TranslateService).use('de');
    const f = TestBed.createComponent(HostComponent);
    const h = f.componentInstance;
    h.s.loadSheet({
      plies: [
        { w: 0, written: 'e4', san: 'e4', uci: 'e2e4', match: 'written', uncertain: false },
        { w: 1, written: 'e5', san: 'e5', uci: 'e7e5', match: 'written', uncertain: true },
        { w: 2, written: 'Sf3', san: 'Nf3', uci: 'g1f3', match: 'written', uncertain: false },
      ],
      unresolved: [], unresolvedFrom: null, boxes: [[10, 10, 50, 30], [60, 10, 100, 30], null], written: ['e4', 'e5', 'Sf3'], pages: [],
    });
    h.ph.photoUrls.set(['blob:seite1']);
    h.ph.viewCounts.set([1]);
    f.detectChanges();
    return { f, h, el: f.nativeElement as HTMLElement };
  }

  it('teilt wie die LeagueHub-Prüfseite auf: Foto | Brett | Prüfen + Zugliste, am Handy Prüfen zuerst', () => {
    const { el } = setup();
    const css = ((SheetWorkbenchComponent as unknown as { ɵcmp: { styles: string[] } }).ɵcmp.styles).join('\n')
      .replace(/\[_ngcontent-[^\]]*\]/g, '').replace(/\s+/g, ' ');
    expect(css).toContain('grid-template-areas: "photo board check" "photo board moves"');
    // UX-036: am Handy als Reihenfolge Prüfen → Brett → Seitenteil des Aufrufers → Zugliste → Foto
    const phone = css.slice(css.indexOf('grid-template-areas: none'));
    const order = ['.scan-check', '.scan-board', '.scan-side', '.scan-moves', '.scan-photo']
      .map(sel => Number(new RegExp(`\\${sel} \\{ ?order: (\\d)`).exec(phone)?.[1]));
    expect(order).toEqual([1, 2, 3, 4, 5]);
    for (const sel of ['.scan-photo', '.scan-board', '.scan-check', '.scan-moves']) expect(el.querySelector(sel)).withContext(sel).not.toBeNull();
    // ohne Seitenteil (RookHub) bleibt keine leere Zeile
    expect(getComputedStyle(el.querySelector('.scan-side')!).display).toBe('none');
  });

  // b1-l-korrigieren-zugliste-kasten (aus LeagueHub übernommen): einspaltig läuft die Zugliste mit der Seite, zweispaltig Kasten.
  it('Zugliste einspaltig ohne eigenen Rollbereich, sonst als Kasten; Brett höchstens 62vh', () => {
    const css = ((SheetWorkbenchComponent as unknown as { ɵcmp: { styles: string[] } }).ɵcmp.styles).join('\n')
      .replace(/\[_ngcontent-[^\]]*\]/g, '').replace(/\s+/g, ' ');
    expect(css).toMatch(/\.moves \{[^}]*max-height: 52vh; overflow-y: auto;/);
    expect(css).toMatch(/@container \(max-width: 720px\) \{ \.moves \{ max-height: none; overflow-y: visible; \} \}/);
    expect(css).toContain('.scan-board app-chess-board, .scan-board .eval-curve { width: min(100%, 62vh); }');
  });

  it('Foto 1 / Foto 2 wechselt auf das zweite Exemplar; die Markierung gehört nur zum ersten', async () => {
    const { f, h, el } = setup();
    const views = Array.from(el.querySelectorAll('.views button')) as HTMLButtonElement[];
    expect(views.map(b => b.textContent?.trim())).toEqual(['sheetEdit.photo', 'sheetEdit.photo']);   // ohne geladene Texte: Schlüssel
    views[1].click();
    await f.whenStable();
    f.detectChanges();
    expect(h.ph.shownView()).toBe(1);
    expect(el.querySelector('.photo-mark')).toBeNull();
  });

  it('zeigt den gewählten Zug mit „Stimmt so" und den Seiteninhalt der Plätze', () => {
    const { h, el, f } = setup();
    expect(h.s.cursor()).toBe(1);                                         // die unsichere Stelle
    expect(el.querySelector('.where .where-actions .confirm')).not.toBeNull();
    expect(el.querySelector('.photo-bar .tool')).not.toBeNull();
    expect(el.querySelector('.scan-check .ply-extra')).not.toBeNull();
    expect(el.querySelector('.end-extra')).toBeNull();
    h.s.go(h.s.legalCount());                                              // Ende der Partie
    f.detectChanges();
    expect(el.querySelector('.scan-check .end-extra')).not.toBeNull();
  });

  it('die Zugliste zeigt die Figurenbuchstaben der Sprache, die Pfeiltasten blättern', () => {
    const { el, h, f } = setup();
    const shown = Array.from(el.querySelectorAll('.moves .ply:not(.end)')).map(b => b.textContent?.trim());
    expect(shown).toEqual(['e4', 'e5', 'Sf3']);
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight' }));
    f.detectChanges();
    expect(h.s.cursor()).toBe(2);
  });

  it('mit Analyse der gespeicherten Partie die Bewertungskurve; ein Klick springt zum Zug', () => {
    const { f, h, el } = setup();
    expect(el.querySelector('.eval-curve')).toBeNull();
    h.evals.set({ status: 'done', analyzed: 3, total: 3, targetDepth: 20, final: { cp: 0, mate: null },
      plies: [20, 900, 15].map((cp, ply) => ({ ply, cp, mate: null, depth: 20, playedUci: '' })) } as GameEvals);
    f.detectChanges();
    expect(el.querySelector('.eval-curve app-eval-graph')).not.toBeNull();
  });
});

import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { EMPTY, of } from 'rxjs';
import { RepertoireTrainerComponent } from './repertoire-trainer.component';
import { RepertoireTrainingService } from './repertoire-training.service';
import { RepertoireExplorerService, DEFAULT_EXPLORER_SETTINGS } from './repertoire-explorer.service';
import { StockfishService } from '../puzzles/stockfish.service';

/** Gerenderte Lern-Ansicht des Trainers (UI-Sweep 2026-10-12, v1-rk-trainer-learn-*). */
describe('RepertoireTrainerComponent (gerendert, Lernmodus)', () => {
  afterEach(() => localStorage.removeItem('rookhub_rep_train_chaptercolor_1'));

  async function render(pgn: string): Promise<HTMLElement> {
    localStorage.setItem('rookhub_rep_train_chaptercolor_1', JSON.stringify({ 'Chapter A': 'w', '?': 'w' }));
    await TestBed.configureTestingModule({
      imports: [RepertoireTrainerComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
        provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }),
        { provide: ActivatedRoute, useValue: { snapshot: {
          paramMap: { get: () => '1' },
          queryParamMap: { get: (k: string) => k === 'mode' ? 'learn' : null },
        } } },
        { provide: RepertoireTrainingService, useValue: { getPgn: () => of(pgn), getLineStates: () => of([]) } },
        { provide: StockfishService, useValue: { init: () => Promise.resolve(), getEval: () => Promise.resolve('') } },
        { provide: RepertoireExplorerService, useValue: { run: () => EMPTY, effectiveSettings: () => of(DEFAULT_EXPLORER_SETTINGS) } },
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(RepertoireTrainerComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('zeigt den Kommentar zum vorgezeigten Zug nur einmal (Kontextkarte), nicht noch als Kommentarbox', async () => {
    const el = await render('[Event "Rep"]\n[White "1.e4"]\n[Black "Chapter A"]\n\n1. e4 {Weiß besetzt das Zentrum.} e5 2. Nf3 *');
    expect(el.querySelectorAll('.ctx-comment').length).toBe(1);
    expect(el.querySelector('.ctx-comment')!.textContent).toContain('Weiß besetzt das Zentrum.');
    expect(el.querySelectorAll('.comment-box').length).toBe(0);
    expect(el.querySelector('.tap-hint')).not.toBeNull();   // Halt bis zum Weitertippen bleibt
  });

  it('ein PGN-Platzhalter „?" als Linienname erscheint nicht', async () => {
    const el = await render('[Event "?"]\n[White "?"]\n[Black "?"]\n\n1. e4 e5 2. Nf3 *');
    expect(el.querySelector('.line-label')).toBeNull();
    expect(el.querySelector('.line-moves')).not.toBeNull();
  });
});

/** Brettgröße des Trainers (UI-Sweep a1). Karma läuft 1 400 px breit — Media-Query-Regeln als CSS-Text geprüft. */
describe('RepertoireTrainerComponent Brettgröße (a1)', () => {
  const css = ((RepertoireTrainerComponent as unknown as { ɵcmp: { styles: string[] } }).ɵcmp.styles)
    .join('\n').replace(/%NS%/g, '').replace(/\s/g, '');
  function block(media: string): string {
    const at = css.indexOf(media);
    expect(at).withContext(media).toBeGreaterThan(-1);
    return css.slice(at, css.indexOf('}}', at) + 2);
  }

  // a1-rk-trainer-brett-hoehe
  it('Laptop: Brett höchstens Fensterhöhe − 200 px (keine Mini-Scrollleiste)', () => {
    expect(css).toMatch(/app-puzzle-board(\[[^\]]*\])?\{flex:11480px;max-width:min\(820px,(calc\()?100vh-200px\)?\)/);
  });

  it('einspaltig: Brett volle Zeile bis Fensterhöhe − 420 px, Infospalte ohne 520-px-Deckel', () => {
    const n = block('@media(max-width:899.98px)');
    expect(n).toMatch(/app-puzzle-board(\[[^\]]*\])?\{flex-basis:100%;max-width:min\(100%,(calc\()?100vh-420px\)?\)/);
    expect(n).toMatch(/\.side(\[[^\]]*\])?\{max-width:none/);
  });

  // a1-rk-trainer-ultrawide-brett
  it('ab 2400 px Brett bis Fensterhöhe (≤ 1 100 px), Seitenspalte ≤ 640 px, mittig', () => {
    const w = block('@media(min-width:2400px)');
    expect(w).toMatch(/app-puzzle-board(\[[^\]]*\])?\{flex:00auto;width:min\((calc\()?100vh-200px\)?,1100px\);max-width:none/);
    expect(w).toMatch(/\.side(\[[^\]]*\])?\{max-width:640px/);
    expect(w).toMatch(/\.play(\[[^\]]*\])?\{justify-content:center/);
    expect(css.slice(0, css.indexOf('@media(min-width:2400px)'))).not.toContain('1100px');
  });
});

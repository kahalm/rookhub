import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { provideTranslateService } from '@ngx-translate/core';
import { GameAnalysisNodesDialogComponent, GameAnalysisNodesDialogData } from './game-analysis-nodes-dialog.component';

describe('GameAnalysisNodesDialogComponent', () => {
  const engines = [{ id: 'eei_sf', name: 'Stockfish 18' }, { id: 'rhe_lc', name: 'Lc0 (Spark)' }];

  async function make(over: Partial<GameAnalysisNodesDialogData> = {}) {
    const ref = { close: jasmine.createSpy('close') };
    await TestBed.configureTestingModule({
      imports: [GameAnalysisNodesDialogComponent],
      providers: [
        provideRouter([]), provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }),
        { provide: MatDialogRef, useValue: ref },
        { provide: MAT_DIALOG_DATA, useValue: { engines, depth: 30, lines: 3, ...over } },
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(GameAnalysisNodesDialogComponent);
    fixture.detectChanges();
    return { fixture, c: fixture.componentInstance, ref };
  }

  it('waehlt lc0 vor, wenn eine Engine so heisst, und 50 000 Knoten', async () => {
    const { c } = await make();
    expect(c.engineId).toBe('rhe_lc');
    expect(c.nodes).toBe(50_000);
    expect(c.lines).toBe(3);
    expect(c.alsoDepth).toBeTrue();
  });

  it('nimmt die erste Engine, wenn keine lc0 heisst', async () => {
    const { c } = await make({ engines: [{ id: 'eei_sf', name: 'Stockfish 18' }] });
    expect(c.engineId).toBe('eei_sf');
  });

  it('gibt die Wahl zurueck', async () => {
    const { c, ref } = await make();
    c.choice = 100_000;
    c.lines = 2;
    c.alsoDepth = false;
    c.submit();
    expect(ref.close).toHaveBeenCalledWith({ engineId: 'rhe_lc', nodes: 100_000, multiPv: 2, alsoDepth: false });
  });

  it('eigener Wert: nur im Bereich 1 000 bis 50 000 000', async () => {
    const { c, ref } = await make();
    c.choice = c.custom;
    for (const bad of [null, 999, 50_000_001, 1.5]) {
      c.customNodes = bad;
      expect(c.valid).withContext(String(bad)).toBeFalse();
      c.submit();
    }
    expect(ref.close).not.toHaveBeenCalled();

    c.customNodes = 750_000;
    expect(c.valid).toBeTrue();
    c.submit();
    expect(ref.close).toHaveBeenCalledWith(jasmine.objectContaining({ nodes: 750_000 }));
  });

  it('ohne Hintergrund-Engine: Hinweis aufs Profil, kein Starten', async () => {
    const { fixture, c, ref } = await make({ engines: [] });
    c.submit();
    expect(ref.close).not.toHaveBeenCalled();
    expect((fixture.nativeElement as HTMLElement).querySelector('a[href="/profile"]')).not.toBeNull();
  });

  // d7-rk-lc0-dialog-info-icon-abgeschnitten
  it('ⓘ bleibt bei zweizeiligem Hinweis volle 24 px breit', async () => {
    const { fixture } = await make({ engines: [] });
    const host = fixture.nativeElement as HTMLElement;
    host.style.cssText = 'display:block;width:300px';
    document.body.appendChild(host);
    const p = host.querySelector('p.warn') as HTMLElement;
    p.append(' ' + 'Im Profil ist keine Hintergrund-Engine festgelegt, bitte dort eine wählen. '.repeat(3));
    const icon = p.querySelector('mat-icon') as HTMLElement;
    expect(icon.textContent!.trim()).toBe('info');
    expect(icon.getBoundingClientRect().width).toBeGreaterThanOrEqual(24);
    expect(getComputedStyle(icon).flexShrink).toBe('0');
    host.remove();
  });
});

describe('gameAnalysis.nodes.title schreibt „Lc0“', () => {
  for (const lang of ['de', 'en', 'hr', 'hu']) {
    it(lang, async () => {
      const d = await (await fetch(`/i18n/${lang}.json`).then(r => r.ok ? r : fetch(`/base/i18n/${lang}.json`))).json();
      expect(d.gameAnalysis.nodes.title).toContain('Lc0');
      expect(d.gameAnalysis.nodes.title).not.toContain('lc0');
    });
  }
});

import { TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { MatDialog } from '@angular/material/dialog';
import { provideTranslateService, TranslateService } from '@ngx-translate/core';
import { EbookPositionDialogComponent } from './ebook-position-dialog.component';

/**
 * d7-rk-stellung-korrigieren-dialog-abgeschnitten: „Übernehmen“/„Abbrechen“ stehen in mat-dialog-actions und sind bei jeder
 * Fenstergröße sichtbar, das Brett passt ohne Scrollen in den Dialog. Der Spec misst am echten Dialog im Testbrowser
 * (karma.conf: 1400×900); dieselben Erwartungen wurden zusätzlich mit 1920×1080, 1366×768 und 820×1180 (Tablet) gefahren.
 */
describe('EbookPositionDialogComponent (Layout)', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [EbookPositionDialogComponent],
      providers: [provideNoopAnimations(), provideTranslateService({ fallbackLang: 'de' })],
    }).compileComponents();
    const t = TestBed.inject(TranslateService);
    t.setTranslation('de', {
      common: { cancel: 'Abbrechen', whiteToMove: 'Weiß am Zug', blackToMove: 'Schwarz am Zug', moreActions: 'Weitere Aktionen' },
      analysis: { flip: 'Brett drehen', setup: { apply: 'Übernehmen', erase: 'Radierer', clear: 'Brett leeren', startpos: 'Grundstellung' } },
      ebooks: { board: { correctTitle: 'Stellung korrigieren',
        correctHint: 'Figuren setzen, verschieben oder entfernen und die Seite am Zug wählen. Die Korrektur wird gespeichert und hilft, die Diagramm-Erkennung zu verbessern.' } },
    });
    t.use('de');
  });

  afterEach(() => TestBed.inject(MatDialog).closeAll());

  async function open() {
    const ref = TestBed.inject(MatDialog).open(EbookPositionDialogComponent,
      { data: { fen: '4k3/8/8/8/8/8/8/4K3 w - - 0 1' }, width: '32rem', maxWidth: '94vw', autoFocus: 'dialog' });
    for (let i = 0; i < 3; i++) { TestBed.tick(); await new Promise(r => setTimeout(r, 30)); }
    return ref;
  }

  it('Knöpfe stehen in mat-dialog-actions, sind im Fenster sichtbar und der Inhalt scrollt nicht', async () => {
    await open();
    const actions = document.querySelector('.cdk-overlay-container mat-dialog-actions') as HTMLElement;
    expect(actions).withContext('mat-dialog-actions').toBeTruthy();
    const apply = actions.querySelector('.pos-apply') as HTMLElement;
    const cancel = actions.querySelector('.pos-cancel') as HTMLElement;
    expect(apply?.textContent).toContain('Übernehmen');
    expect(cancel?.textContent).toContain('Abbrechen');
    expect(cancel.querySelector('mat-icon')).withContext('Abbrechen ohne ✕').toBeNull();
    // Die eigene Knopfzeile des Editors ist ausgeblendet — keine doppelten Knöpfe.
    expect(document.querySelector('.cdk-overlay-container .ps-actions')).toBeNull();
    for (const b of [apply, cancel]) {
      const r = b.getBoundingClientRect();
      expect(r.top).toBeGreaterThanOrEqual(0);
      expect(r.bottom).withContext(`Knopf unten bei ${window.innerWidth}×${window.innerHeight}`).toBeLessThanOrEqual(window.innerHeight);
    }
    const content = document.querySelector('.cdk-overlay-container mat-dialog-content') as HTMLElement;
    expect(content.scrollHeight - content.clientHeight).withContext('Inhalt scrollt nicht').toBeLessThanOrEqual(1);
    const board = document.querySelector('.cdk-overlay-container .ps-board') as HTMLElement;
    const br = board.getBoundingClientRect();
    expect(br.top).toBeGreaterThanOrEqual(0);
    expect(br.width).toBeGreaterThanOrEqual(200);
    // Werkzeugzeile in EINER Zeile (sonst rutschte „Brett leeren“ als ✕ allein darunter, d7-…-x-knopf-unklar).
    const tools = document.querySelector('.cdk-overlay-container .ps-tools') as HTMLElement;
    if (window.innerWidth >= 600) expect(tools.getBoundingClientRect().height).toBeLessThanOrEqual(56);
  });

  it('„Übernehmen“ in der Dialogleiste schließt mit der FEN des Editors', async () => {
    const ref = await open();
    let result: string | undefined;
    ref.afterClosed().subscribe(f => result = f);
    (document.querySelector('.cdk-overlay-container .pos-apply') as HTMLElement).click();
    TestBed.tick();
    await new Promise(r => setTimeout(r, 30));
    expect(result).toBe('4k3/8/8/8/8/8/8/4K3 w - - 0 1');
  });
});

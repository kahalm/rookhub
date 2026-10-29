import { ChangeDetectionStrategy, Component } from '@angular/core';
import { ComponentType } from '@angular/cdk/portal';
import { TestBed } from '@angular/core/testing';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { TranslateService, provideTranslateService } from '@ngx-translate/core';
import { CreateTokenDialogComponent } from './profile/api-tokens.component';
import { CalcEditionDialogComponent } from './courses/calc-edition-dialog.component';

/**
 * Gesperrter Dialogknopf mit Grund (DESIGN 5.6, d8-rk-dialog-aktionen-umbruch / d8-rk-dialog-aktionen-zweite-zeile /
 * d8-rk-ausgabe-dialog-grund-umbruch): der Grund `.why` steht links in der Knopfleiste und schrumpft — die globale Regel
 * in styles.scss. Vorher behielt er seine volle Textbreite, und der Hauptknopf rutschte allein in Zeile 2.
 */
@Component({
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatDialogModule, MatButtonModule, MatIconModule],
  template: `
    <h2 mat-dialog-title>Wochenpost aus Buch-Kapitel</h2>
    <mat-dialog-content>Inhalt</mat-dialog-content>
    <mat-dialog-actions align="end">
      <span class="why" role="status">Erst ein Buch und ein Kapitel wählen.</span>
      <button mat-button class="cancel">Abbrechen</button>
      <button mat-flat-button color="primary" class="main" disabled><mat-icon>add</mat-icon> Anlegen</button>
    </mat-dialog-actions>
  `,
})
class WhyDialogHostComponent {}

const top = (sel: string) => (document.querySelector(`.cdk-overlay-container ${sel}`) as HTMLElement).getBoundingClientRect().top;

describe('Dialog-Knopfleiste mit Grund (styles.scss, DESIGN 5.6)', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' })] });
    const t = TestBed.inject(TranslateService);
    t.setTranslation('de', {
      common: { cancel: 'Abbrechen', save: 'Speichern', delete: 'Löschen', days: 'Tage' },
      weekly: { fields: { time: 'Uhrzeit' } },
      profile: { tokens: { dialog: { needName: 'Erst einen Namen eingeben.', create: 'Erstellen', title: 'Neuer API-Token' } } },
      calc: { series: { publishRequired: 'Erst den Freigabe-Zeitpunkt wählen.', publishAt: 'Freigabe-Zeitpunkt',
        newEdition: 'Neue Ausgabe', editEdition: 'Ausgabe bearbeiten', videoUrl: 'Video-Link' } },
    });
    t.use('de');
  });
  afterEach(() => TestBed.inject(MatDialog).closeAll());

  async function open<T>(cmp: ComponentType<T>, config: object) {
    TestBed.inject(MatDialog).open(cmp, { maxWidth: '94vw', ...config });
    for (let i = 0; i < 3; i++) { TestBed.tick(); await new Promise(r => setTimeout(r, 30)); }
  }

  it('Wochenpost-Muster (480 px): Grund bricht in sich um, „Abbrechen“ und „Anlegen“ bleiben in Zeile 1', async () => {
    await open(WhyDialogHostComponent, { width: '480px' });
    const why = document.querySelector('.cdk-overlay-container .why') as HTMLElement;
    const s = getComputedStyle(why);
    expect(s.flexGrow).toBe('1');
    expect(s.minWidth).toBe('0px');
    // Farbe = on-surface-variant (Ton von mat-hint), gemessen gegen eine Probe im selben Dialog.
    const probe = document.createElement('span');
    probe.style.color = 'var(--mat-sys-on-surface-variant)';
    why.parentElement!.appendChild(probe);
    expect(s.color).toBe(getComputedStyle(probe).color);
    probe.remove();
    expect(top('.main')).toBe(top('.cancel'));
    // Der Grund steht in derselben Zeile wie die Knöpfe, nicht darüber.
    expect(why.getBoundingClientRect().top).toBeLessThan((document.querySelector('.cdk-overlay-container .cancel') as HTMLElement)
      .getBoundingClientRect().bottom);
  });

  it('Token-Dialog (420 px): „Erstellen“ steht neben „Abbrechen“, nicht allein darunter', async () => {
    await open(CreateTokenDialogComponent, { width: '420px' });
    const buttons = Array.from(document.querySelectorAll<HTMLElement>('.cdk-overlay-container mat-dialog-actions button'));
    expect(buttons.length).toBe(2);
    expect(document.querySelector('.cdk-overlay-container mat-dialog-actions .why')?.textContent).toContain('Erst einen Namen');
    expect(buttons[0].getBoundingClientRect().top).toBe(buttons[1].getBoundingClientRect().top);
  });

  it('Ausgabe-Dialog mit Löschen (440 px): der Grund bekommt eine eigene Zeile über den Knöpfen, die Knöpfe bleiben zusammen', async () => {
    await open(CalcEditionDialogComponent, { width: '440px', data: { chapter: 'KW 7',
      edition: { id: 1, bookId: 2, chapter: 'KW 7', videoUrl: null, publishAt: 'kaputt', testerPreviewAt: null, released: false } } });
    const why = document.querySelector('.cdk-overlay-container mat-dialog-actions .why') as HTMLElement;
    expect(why).not.toBeNull();
    const buttons = Array.from(document.querySelectorAll<HTMLElement>('.cdk-overlay-container mat-dialog-actions button'));
    expect(buttons.length).toBe(3);   // Löschen, Abbrechen, Speichern
    const tops = buttons.map(b => b.getBoundingClientRect().top);
    expect(new Set(tops).size).toBe(1);
    expect(why.getBoundingClientRect().bottom).toBeLessThanOrEqual(tops[0] + 1);
  });
});

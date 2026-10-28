import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { CreateTokenDialogComponent } from '../features/profile/api-tokens.component';
import { CalcEditionDialogComponent } from '../features/courses/calc-edition-dialog.component';
import { setEinkActive } from './eink';

/** Reiner Text-Dialog (wie die Rückfrage): Titel + Inhalt ohne Formfeld. */
@Component({
  standalone: true,
  imports: [MatDialogModule],
  template: `<h2 mat-dialog-title>Titel</h2><mat-dialog-content><p class="txt">Nur Text.</p></mat-dialog-content>`,
})
class TextOnlyDialog {}

/** Kopfzeile mit ✕ (Titel in einer Hülle wie die Endlos-Hilfe), einmal mit Formfeld. */
@Component({
  standalone: true,
  imports: [MatDialogModule, MatFormFieldModule, MatInputModule],
  template: `<div class="hdr"><h2 mat-dialog-title>Titel</h2><button mat-dialog-close>x</button></div>
    <mat-dialog-content>
      @if (withField) { <mat-form-field appearance="outline"><mat-label>Name</mat-label><input matInput value="x"></mat-form-field> }
      @else { <p>Nur Text.</p> }
    </mat-dialog-content>`,
})
class HeaderDialog { withField = false; }

/**
 * Globale Dialog-Regeln in `src/styles.scss` (UI-Sweep d7, DESIGN 5.6), gemessen an echten MatDialogs mit den
 * geladenen globalen Stilen:
 * - erstes Outline-Feld unter dem Titel: die schwebende Beschriftung liegt ganz im Inhalt (vorher halb abgeschnitten);
 * - Dunkel: Dialogfläche heller als die Seite + 1-px-Rahmen; E-Ink behält seinen 2-px-Rahmen.
 */
describe('styles.scss — Dialoge (d7)', () => {
  let dialog: MatDialog;
  let wasDark: boolean;

  beforeEach(() => {
    wasDark = document.documentElement.classList.contains('dark-theme');
    TestBed.configureTestingModule({
      providers: [provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' })],
    });
    dialog = TestBed.inject(MatDialog);
  });
  afterEach(() => {
    dialog.closeAll();
    setEinkActive(false);
    document.documentElement.classList.toggle('dark-theme', wasDark);
    TestBed.resetTestingModule();
  });

  async function settle(): Promise<void> {
    await new Promise(r => setTimeout(r, 0));
    await new Promise(r => requestAnimationFrame(() => r(null)));
  }

  function lastSurface(): HTMLElement {
    const all = document.querySelectorAll<HTMLElement>('.mat-mdc-dialog-surface');
    return all[all.length - 1];
  }

  function expectFirstLabelInside(surface: HTMLElement): void {
    const content = surface.querySelector('.mat-mdc-dialog-content') as HTMLElement;
    const label = surface.querySelector('.mdc-floating-label--float-above') as HTMLElement;
    expect(label).withContext('schwebende Beschriftung').not.toBeNull();
    const c = content.getBoundingClientRect();
    const l = label.getBoundingClientRect();
    expect(l.height).toBeGreaterThan(0);
    // Die Beschriftung beginnt nicht über der Oberkante des Scrollbereichs (der sie sonst abschneidet).
    expect(l.top).withContext(`label ${l.top} / content ${c.top}`).toBeGreaterThanOrEqual(c.top - 0.5);
    expect(parseFloat(getComputedStyle(content).paddingTop)).toBe(8);
  }

  it('d7-rk-token-dialog-label-abgeschnitten: „Name*“ im Token-Dialog liegt ganz im Inhalt', async () => {
    const ref = dialog.open(CreateTokenDialogComponent, { width: '440px' });
    ref.componentInstance.name = 'Laptop';
    ref.componentRef!.changeDetectorRef.detectChanges();
    await settle();
    ref.componentRef!.changeDetectorRef.detectChanges();
    await settle();
    expectFirstLabelInside(lastSurface());
  });

  it('d7-rk-dialog-erstes-feld-label-abgeschnitten: „Video-URL“ in „Neue Ausgabe“ liegt ganz im Inhalt', async () => {
    const ref = dialog.open(CalcEditionDialogComponent, {
      width: '440px',
      data: { chapter: 'KW 8', edition: { id: 1, bookId: 1, chapter: 'KW 8', videoUrl: 'https://youtu.be/x',
        publishAt: '2026-09-07T16:30:00.000Z', testerPreviewAt: null, released: false } },
    });
    ref.componentRef!.changeDetectorRef.detectChanges();
    await settle();
    ref.componentRef!.changeDetectorRef.detectChanges();
    await settle();
    expectFirstLabelInside(lastSurface());
  });

  it('reiner Text-Dialog (Rückfrage) bekommt kein zusätzliches Polster unter dem Titel', async () => {
    dialog.open(TextOnlyDialog);
    await settle();
    const content = lastSurface().querySelector('.mat-mdc-dialog-content') as HTMLElement;
    expect(parseFloat(getComputedStyle(content).paddingTop)).toBe(0);
  });

  it('Kopfzeile mit ✕: Inhalt ohne Polster oben wie beim schlichten Titel, mit Formfeld 8 px', async () => {
    dialog.open(HeaderDialog);
    await settle();
    let content = lastSurface().querySelector('.mat-mdc-dialog-content') as HTMLElement;
    expect(parseFloat(getComputedStyle(content).paddingTop)).toBe(0);
    dialog.closeAll();
    await settle();

    const ref = dialog.open(HeaderDialog);
    ref.componentInstance.withField = true;
    ref.componentRef!.changeDetectorRef.detectChanges();
    await settle();
    content = lastSurface().querySelector('.mat-mdc-dialog-content') as HTMLElement;
    expect(parseFloat(getComputedStyle(content).paddingTop)).toBe(8);
  });

  it('rh-scroll-hint: Weiterscroll-Schatten global (local-Abdeckung + scroll-Schatten)', async () => {
    @Component({ standalone: true, imports: [MatDialogModule],
      template: `<h2 mat-dialog-title>T</h2><mat-dialog-content class="rh-scroll-hint"><p>x</p></mat-dialog-content>` })
    class HintDialog {}
    dialog.open(HintDialog);
    await settle();
    const cs = getComputedStyle(lastSurface().querySelector('.mat-mdc-dialog-content') as HTMLElement);
    expect(cs.backgroundImage).toContain('linear-gradient');
    expect(cs.backgroundImage).toContain('radial-gradient');
    expect(cs.backgroundAttachment).toBe('local, scroll');
  });

  it('d7-rk-dialog-dunkel-kaum-abgesetzt: dunkel ist die Fläche heller als die Seite und hat einen 1-px-Rahmen', async () => {
    document.documentElement.classList.add('dark-theme');
    dialog.open(TextOnlyDialog);
    await settle();
    const surface = lastSurface();
    const cs = getComputedStyle(surface);
    expect(cs.backgroundColor).not.toBe(getComputedStyle(document.body).backgroundColor);
    expect(cs.borderTopWidth).toBe('1px');
    expect(cs.borderTopStyle).toBe('solid');
  });

  it('hell bleibt die Dialogfläche ohne Rahmen (Material-Vorgabe)', async () => {
    document.documentElement.classList.remove('dark-theme');
    dialog.open(TextOnlyDialog);
    await settle();
    expect(getComputedStyle(lastSurface()).borderTopWidth).toBe('0px');
  });

  it('E-Ink: der 2-px-Rahmen gewinnt auch dann, wenn die Klasse dark-theme noch steht', async () => {
    document.documentElement.classList.add('dark-theme');
    setEinkActive(true);
    dialog.open(TextOnlyDialog);
    await settle();
    const cs = getComputedStyle(lastSurface());
    expect(cs.borderTopWidth).toBe('2px');
    expect(cs.borderTopColor).toBe('rgb(0, 0, 0)');
  });
});

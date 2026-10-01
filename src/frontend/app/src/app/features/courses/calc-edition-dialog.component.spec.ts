import { TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { CalcEditionDialogComponent, CalcEditionDialogData, CalcEditionDialogResult } from './calc-edition-dialog.component';
import { CalcEdition } from './calc-editions.service';

const EDITION: CalcEdition = {
  id: 4, bookId: 12, chapter: 'KW 7', videoUrl: 'https://youtu.be/abc',
  publishAt: '2026-09-07T16:30:00.000Z', testerPreviewAt: '2026-09-05T08:00:00.000Z', released: false,
};

async function open(data: CalcEditionDialogData) {
  const closed: (CalcEditionDialogResult | undefined)[] = [];
  await TestBed.configureTestingModule({
    imports: [CalcEditionDialogComponent],
    providers: [
      provideNoopAnimations(),
      provideTranslateService({ fallbackLang: 'en' }),
      { provide: MAT_DIALOG_DATA, useValue: data },
      { provide: MatDialogRef, useValue: { close: (v?: CalcEditionDialogResult) => closed.push(v) } },
    ],
  }).compileComponents();
  const fixture = TestBed.createComponent(CalcEditionDialogComponent);
  fixture.detectChanges();
  await fixture.whenStable();
  const el = fixture.nativeElement as HTMLElement;
  const button = (label: string) => Array.from(el.querySelectorAll<HTMLButtonElement>('mat-dialog-actions button'))
    .find(b => b.textContent!.includes(label)) ?? null;
  return { fixture, c: fixture.componentInstance, closed, el, button };
}

describe('CalcEditionDialogComponent', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('neue Ausgabe: ohne Löschen, Speichern erst mit Veröffentlichungszeit', async () => {
    const { fixture, c, el, button, closed } = await open({ chapter: 'KW 8' });

    expect(el.querySelector('h2')!.textContent).toContain('calc.series.newEdition');
    expect(button('common.delete')).toBeNull();
    expect(button('common.save')!.disabled).toBeTrue();

    c.publishLocal = '2026-09-14T18:00';
    c.videoUrl = '   ';
    fixture.detectChanges();
    expect(button('common.save')!.disabled).toBeFalse();
    button('common.save')!.click();

    // Die Eingabe ist LOKALE Zeit, gesendet wird UTC; ein nur aus Leerzeichen bestehender Link ist keiner.
    expect(closed).toEqual([{ save: {
      chapter: 'KW 8',
      videoUrl: null,
      publishAt: new Date('2026-09-14T18:00').toISOString(),
      testerPreviewAt: null,
    } }]);
  });

  it('bestehende Ausgabe: füllt die Felder vor, und unverändert gespeichert kommen dieselben Zeitpunkte zurück', async () => {
    const { c, el, button, closed } = await open({ chapter: EDITION.chapter, edition: EDITION });

    expect(el.querySelector('h2')!.textContent).toContain('calc.series.editEdition');
    expect(c.videoUrl).toBe('https://youtu.be/abc');
    expect(c.publishLocal).toMatch(/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}$/);
    // Hin- und Rückweg über die lokale Zeitzone darf keinen Zeitpunkt verschieben.
    button('common.save')!.click();

    expect(closed).toEqual([{ save: {
      chapter: 'KW 7',
      videoUrl: 'https://youtu.be/abc',
      publishAt: EDITION.publishAt,
      testerPreviewAt: EDITION.testerPreviewAt!,
    } }]);
  });

  it('Löschen schließt mit dem Löschauftrag, Abbrechen ohne Wert', async () => {
    const first = await open({ chapter: EDITION.chapter, edition: EDITION });
    first.button('common.delete')!.click();
    expect(first.closed).toEqual([{ delete: true }]);
    TestBed.resetTestingModule();

    const second = await open({ chapter: EDITION.chapter, edition: EDITION });
    second.button('common.cancel')!.click();
    expect(second.closed).toEqual([undefined]);
  });

  it('speichert nichts, wenn die Veröffentlichungszeit keine gültige Zeit ist', async () => {
    const { c, closed } = await open({ chapter: 'KW 8', edition: { ...EDITION, publishAt: 'kaputt', testerPreviewAt: null } });

    expect(c.publishLocal).toBe('');
    c.publishLocal = 'kein Datum';
    c.onSave();

    expect(closed).toEqual([]);
  });
});

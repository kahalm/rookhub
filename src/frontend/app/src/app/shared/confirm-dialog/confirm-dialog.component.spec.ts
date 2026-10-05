import { TestBed } from '@angular/core/testing';
import { MatDialog, MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { OverlayContainer } from '@angular/cdk/overlay';
import { of, Subject } from 'rxjs';
import { CONFIRM_LABELS, ConfirmData, ConfirmDialogComponent, ConfirmService } from './confirm-dialog.component';

/**
 * Die EINE Rückfrage aller Oberflächen (Codereview W5 F8-005). LeagueHub und ClubHub stellen keine
 * Sprache ein — ihre Knöpfe kommen deshalb aus `CONFIRM_LABELS`, sonst stünde unter der deutschen
 * Frage „Cancel"/„OK" aus der englischen Rückfall-Übersetzung.
 */
describe('ConfirmService', () => {
  function setup(labels?: { confirm: string; cancel: string }) {
    TestBed.configureTestingModule({
      providers: [
        provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
        ...(labels ? [{ provide: CONFIRM_LABELS, useValue: labels }] : []),
      ],
    });
    const open = spyOn(TestBed.inject(MatDialog), 'open')
      .and.returnValue({ afterClosed: () => of(true) } as MatDialogRef<unknown>);
    return { service: TestBed.inject(ConfirmService), open };
  }

  it('ohne Vorgabe: Knöpfe aus der Übersetzung, ein schon übersetzter Text geht unverändert durch', () => {
    const { service, open } = setup();
    let answer: boolean | undefined;
    service.ask('Dieses Foto löschen?').subscribe(ok => answer = ok);

    expect(answer).toBeTrue();
    const data = open.calls.mostRecent().args[1]!.data as ConfirmData;
    expect(data.message).toBe('Dieses Foto löschen?');
    expect(data.confirmLabel).toBeUndefined();
    expect(data.cancelLabel).toBeUndefined();
  });

  it('aus einem modalen <dialog> heraus: Rückfrage hängt im Dialog (klickbar), Esc schließt den Dialog nicht, danach zurück (0.659.1)', () => {
    TestBed.configureTestingModule({ providers: [provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' })] });
    const closed = new Subject<boolean>();
    spyOn(TestBed.inject(MatDialog), 'open').and.returnValue({ afterClosed: () => closed } as unknown as MatDialogRef<unknown>);
    const container = TestBed.inject(OverlayContainer).getContainerElement();
    const before = container.parentElement;
    const dlg = document.createElement('dialog');
    document.body.appendChild(dlg);
    dlg.showModal();
    try {
      let answer: boolean | undefined;
      TestBed.inject(ConfirmService).ask('Konto entfernen?').subscribe(ok => answer = ok);
      expect(container.parentElement).toBe(dlg);
      const cancel = new Event('cancel', { cancelable: true });
      dlg.dispatchEvent(cancel);
      expect(cancel.defaultPrevented).toBeTrue();
      closed.next(true);
      closed.complete();
      expect(answer).toBeTrue();
      expect(container.parentElement).toBe(before);
      const later = new Event('cancel', { cancelable: true });
      dlg.dispatchEvent(later);
      expect(later.defaultPrevented).toBeFalse();
    } finally {
      dlg.close();
      dlg.remove();
    }
  });

  it('mit CONFIRM_LABELS: die festen Knöpfe der Oberfläche', () => {
    const { service, open } = setup({ confirm: 'OK', cancel: 'Abbrechen' });
    service.ask('Diese Notiz löschen?').subscribe();

    const data = open.calls.mostRecent().args[1]!.data as ConfirmData;
    expect(data.confirmLabel).toBe('OK');
    expect(data.cancelLabel).toBe('Abbrechen');
  });

  it('zeigt die Knöpfe, die die Daten nennen', () => {
    TestBed.configureTestingModule({
      imports: [ConfirmDialogComponent],
      providers: [
        provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
        { provide: MAT_DIALOG_DATA, useValue: { message: 'Löschen?', confirmLabel: 'OK', cancelLabel: 'Abbrechen' } },
        { provide: MatDialogRef, useValue: { close: () => {} } },
      ],
    });
    const fixture = TestBed.createComponent(ConfirmDialogComponent);
    fixture.detectChanges();

    const buttons = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('button')).map(b => b.textContent?.trim());
    expect(buttons).toEqual(['Abbrechen', 'OK']);
  });
});

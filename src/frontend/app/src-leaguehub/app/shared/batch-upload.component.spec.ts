import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { BatchUploadComponent, batchErrorText } from './batch-upload.component';
import { ClubClient } from '../core/club-api.service';

describe('BatchUploadComponent', () => {
  let fixture: ComponentFixture<BatchUploadComponent>;
  let client: jasmine.SpyObj<ClubClient>;
  const file = (n: string) => new File([new Uint8Array([1, 2, 3])], n, { type: 'image/jpeg' });

  beforeEach(() => {
    client = jasmine.createSpyObj<ClubClient>('ClubClient', ['batchStart', 'batchFile', 'batchFinish']);
    client.batchStart.and.resolveTo({ key: 'K', files: 0, bytes: 0, finished: false });
    client.batchFile.and.resolveTo({ key: 'K', files: 1, bytes: 3, finished: false });
    fixture = TestBed.createComponent(BatchUploadComponent);
    fixture.componentRef.setInput('client', client);
    fixture.detectChanges();
  });

  it('lädt jedes Bild einzeln in einen Stapel, schließt ab und dankt; ein Fehler hält die übrigen nicht auf', async () => {
    client.batchFile.and.callFake(async (_k: string, f: File) => {
      if (f.name === 'b.txt') throw new HttpErrorResponse({ status: 400, error: { reason: 'type' } });
      return { key: 'K', files: 1, bytes: 3, finished: false };
    });
    client.batchFinish.and.resolveTo({ key: 'K', files: 2, bytes: 6, finished: true });
    const c = fixture.componentInstance;
    c.files.set([file('a.jpg'), file('b.txt'), file('c.jpg')]);
    c.comment.set('Runde 3');
    await c.send();
    fixture.detectChanges();
    expect(client.batchStart).toHaveBeenCalledWith('Runde 3');
    expect(client.batchFile).toHaveBeenCalledTimes(3);
    expect(client.batchFinish).toHaveBeenCalledWith('K');
    const el = fixture.nativeElement as HTMLElement;
    expect(el.textContent).toContain('Danke — 2 Bilder sind abgelegt, die Admins sind informiert.');
    expect(el.textContent).toContain('Nicht hochgeladen: b.txt (nur Bilder oder PDF)');
  });

  it('Tagesdeckel bricht ab; kein Bild durch → Fehlermeldung statt Dank', async () => {
    client.batchFile.and.rejectWith(new HttpErrorResponse({ status: 400, error: { reason: 'dailyLimit' } }));
    client.batchFinish.and.rejectWith(new HttpErrorResponse({ status: 400, error: { reason: 'empty' } }));
    const c = fixture.componentInstance;
    c.files.set([file('a.jpg'), file('b.jpg')]);
    await c.send();
    expect(client.batchFile).toHaveBeenCalledTimes(1);
    expect(c.done()).toBeNull();
    expect(c.error()).toContain('für heute ist über diesen Link genug hochgeladen');
  });

  it('Texte der Gründe', () => {
    expect(batchErrorText('tooLarge')).toBe('zu groß');
    expect(batchErrorText(undefined)).toBe('hat nicht geklappt');
  });
});

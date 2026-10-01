import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ClubApiService } from '../core/club-api.service';
import { Photo } from '../core/club.models';
import { SessionPhotosComponent } from './session-photos.component';
import { of } from 'rxjs';
import { ConfirmService } from '@rh/shared/confirm-dialog/confirm-dialog.component';

const P = (id: number): Photo => ({ id, width: 1600, height: 1200, createdAt: '2026-09-25T17:00:00Z' });

describe('SessionPhotosComponent', () => {
  let fixture: ComponentFixture<SessionPhotosComponent>;
  let api: jasmine.SpyObj<ClubApiService>;
  /** Rückfrage (ConfirmService) — Vorgabe „ja", je Test umstellbar. */
  let confirmAsk: jasmine.Spy;
  const el = () => fixture.nativeElement as HTMLElement;

  async function create(photos: Photo[], editable: boolean): Promise<void> {
    TestBed.configureTestingModule({ imports: [SessionPhotosComponent], providers: [{ provide: ConfirmService, useValue: { ask: (...a: unknown[]) => confirmAsk(...a) } }, { provide: ClubApiService, useValue: api }] });
    fixture = TestBed.createComponent(SessionPhotosComponent);
    fixture.componentRef.setInput('sessionId', 5);
    fixture.componentRef.setInput('photos', photos);
    fixture.componentRef.setInput('editable', editable);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  beforeEach(() => {
    confirmAsk = jasmine.createSpy('ask').and.returnValue(of(true));
    api = jasmine.createSpyObj<ClubApiService>('ClubApiService', ['photoBlob', 'deletePhoto']);
    api.photoBlob.and.resolveTo(new Blob(['jpeg'], { type: 'image/jpeg' }));
    spyOn(URL, 'createObjectURL').and.callFake(() => 'blob:x/' + Math.random());
    spyOn(URL, 'revokeObjectURL');
  });

  it('holt je Foto das VORSCHAUBILD als Blob (die Bilder liegen hinter der Anmeldung) und zeigt es', async () => {
    await create([P(1), P(2)], false);
    expect(api.photoBlob.calls.allArgs()).toEqual([[5, 1, true], [5, 2, true]]);
    expect(el().querySelectorAll('.photo img').length).toBe(2);
    expect(el().querySelector('.photo-del')).toBeNull();
  });

  it('ein Tipp öffnet das große Bild, noch ein Tipp schließt es und gibt die Adresse frei', async () => {
    await create([P(1)], false);
    el().querySelector<HTMLButtonElement>('.photo')!.click();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(api.photoBlob).toHaveBeenCalledWith(5, 1, false);
    expect(el().querySelector('.lightbox img')).not.toBeNull();
    el().querySelector<HTMLButtonElement>('.lightbox-close')!.click();
    fixture.detectChanges();
    expect(el().querySelector('.lightbox')).toBeNull();
    expect(URL.revokeObjectURL).toHaveBeenCalled();
  });

  it('löschen fragt nach und meldet die Kennung — die Seite nimmt das Foto aus ihrer Liste', async () => {
    api.deletePhoto.and.resolveTo();
    await create([P(1), P(2)], true);
    const gone: number[] = [];
    fixture.componentInstance.deleted.subscribe(id => gone.push(id));
    const ask = confirmAsk.and.returnValue(of(false));
    el().querySelectorAll<HTMLButtonElement>('.photo-del')[1].click();
    await fixture.whenStable();
    expect(api.deletePhoto).not.toHaveBeenCalled();
    ask.and.returnValue(of(true));
    el().querySelectorAll<HTMLButtonElement>('.photo-del')[1].click();
    await fixture.whenStable();
    expect(api.deletePhoto).toHaveBeenCalledWith(5, 2);
    expect(gone).toEqual([2]);
    // Die Seite gibt die neue Liste herein → das Vorschaubild wird freigegeben.
    fixture.componentRef.setInput('photos', [P(1)]);
    fixture.detectChanges();
    expect(Object.keys(fixture.componentInstance.thumbs())).toEqual(['1']);
    expect(URL.revokeObjectURL).toHaveBeenCalled();
  });

  it('beim Verlassen werden alle Adressen freigegeben', async () => {
    await create([P(1), P(2)], false);
    fixture.destroy();
    expect((URL.revokeObjectURL as jasmine.Spy).calls.count()).toBe(2);
  });
});

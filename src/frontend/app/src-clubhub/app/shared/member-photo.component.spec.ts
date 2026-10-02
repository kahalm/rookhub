import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ClubApiService } from '../core/club-api.service';
import { MemberPhotoComponent } from './member-photo.component';

describe('MemberPhotoComponent (Porträt am Karteiblatt)', () => {
  let fixture: ComponentFixture<MemberPhotoComponent>;
  let api: jasmine.SpyObj<ClubApiService>;
  let revoke: jasmine.Spy;
  const el = () => fixture.nativeElement as HTMLElement;

  async function create(version: number | null, name: string, zoom = false): Promise<void> {
    TestBed.configureTestingModule({ imports: [MemberPhotoComponent], providers: [{ provide: ClubApiService, useValue: api }] });
    fixture = TestBed.createComponent(MemberPhotoComponent);
    fixture.componentRef.setInput('memberId', 7);
    fixture.componentRef.setInput('version', version);
    fixture.componentRef.setInput('name', name);
    fixture.componentRef.setInput('zoom', zoom);
    await settle();
  }

  async function settle(): Promise<void> {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  beforeEach(() => {
    api = jasmine.createSpyObj<ClubApiService>('ClubApiService', ['memberPhotoBlob']);
    api.memberPhotoBlob.and.callFake(async () => new Blob(['jpeg'], { type: 'image/jpeg' }));
    let n = 0;
    spyOn(URL, 'createObjectURL').and.callFake(() => `blob:bild-${++n}`);
    revoke = spyOn(URL, 'revokeObjectURL');
  });

  it('ohne Bild steht der Anfangsbuchstabe da — und nichts wird geholt', async () => {
    await create(null, ' šime ');
    expect(el().classList).toContain('avatar');
    expect(el().querySelector('.avatar-ph')!.textContent).toBe('Š');
    expect(el().querySelector('img')).toBeNull();
    expect(api.memberPhotoBlob).not.toHaveBeenCalled();
  });

  it('mit Bild: das Vorschaubild; ohne „zoom" ist es kein Knopf (die Kartei-Zeile ist selbst der Link)', async () => {
    await create(100, 'Anna');
    expect(api.memberPhotoBlob.calls.allArgs()).toEqual([[7, true, 100]]);
    expect(el().querySelector('img')!.getAttribute('src')).toBe('blob:bild-1');
    expect(el().querySelector('button')).toBeNull();
  });

  it('ein neues Bild (neue Marke) wird nachgeholt', async () => {
    await create(100, 'Anna');
    fixture.componentRef.setInput('version', 200);
    await settle();
    expect(api.memberPhotoBlob.calls.mostRecent().args).toEqual([7, true, 200]);
    expect(el().querySelector('img')!.getAttribute('src')).toBe('blob:bild-2');
  });

  it('mit „zoom": ein Tipp holt das GROSSE Bild und zeigt es; Schließen (auch Esc) gibt dessen Adresse frei', async () => {
    await create(100, 'Anna Auer', true);
    const open = el().querySelector<HTMLButtonElement>('.avatar-open')!;
    expect(open.getAttribute('aria-label')).toBe('Bild von Anna Auer groß zeigen');
    open.click();
    await settle();
    expect(api.memberPhotoBlob).toHaveBeenCalledWith(7, false, 100);
    const big = el().querySelector<HTMLImageElement>('.lightbox img')!;
    expect([big.getAttribute('src'), big.getAttribute('alt')]).toEqual(['blob:bild-2', 'Bild von Anna Auer']);

    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
    fixture.detectChanges();
    expect(el().querySelector('.lightbox')).toBeNull();
    expect(revoke).toHaveBeenCalledOnceWith('blob:bild-2');                               // das Vorschaubild gehört dem Speicher und bleibt
  });

  it('kommt das große Bild nicht, wird wenigstens das Vorschaubild groß gezeigt', async () => {
    await create(100, 'Anna', true);
    api.memberPhotoBlob.and.rejectWith(new Error('offline'));
    await fixture.componentInstance.open();
    fixture.detectChanges();
    expect(el().querySelector('.lightbox img')!.getAttribute('src')).toBe('blob:bild-1');
    fixture.componentInstance.close();
    expect(revoke).not.toHaveBeenCalled();
  });
});

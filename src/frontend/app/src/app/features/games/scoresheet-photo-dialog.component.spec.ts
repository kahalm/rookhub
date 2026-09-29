import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { MAT_DIALOG_DATA } from '@angular/material/dialog';
import { provideTranslateService } from '@ngx-translate/core';
import { ScoresheetPhotoDialogComponent } from './scoresheet-photo-dialog.component';

describe('ScoresheetPhotoDialogComponent', () => {
  async function setup(data: { gameId: number; page?: number }) {
    await TestBed.configureTestingModule({
      imports: [ScoresheetPhotoDialogComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
        { provide: MAT_DIALOG_DATA, useValue: data },
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(ScoresheetPhotoDialogComponent);
    return { fixture, c: fixture.componentInstance, http: TestBed.inject(HttpTestingController) };
  }
  const blob = (b: number) => new Blob([new Uint8Array([b])], { type: 'image/jpeg' });

  // 0.600.0: die Seitenzahl kommt mit dem ersten Foto (Header X-Page-Count) — aus Partienliste und Partieseite, ohne
  // die Einlesung abzufragen.
  it('learns the page count from the first photo and pages through the sheets without reloading', async () => {
    const { fixture, c, http } = await setup({ gameId: 5 });
    fixture.detectChanges();
    http.expectOne('/api/games/5/photo').flush(blob(1), { headers: { 'X-Page-Count': '2' } });
    fixture.detectChanges();
    expect(c.pageCount()).toBe(2);
    expect(fixture.nativeElement.querySelector('.pager')).not.toBeNull();
    const first = c.url();

    c.show(2);
    http.expectOne('/api/games/5/photo?page=2').flush(blob(2), { headers: { 'X-Page-Count': '2' } });
    expect(c.url()).not.toBe(first);
    c.show(1);                                   // schon geladen: kein zweiter Abruf
    http.expectNone('/api/games/5/photo');
    expect(c.url()).toBe(first);
  });

  it('the edit page opens it on the page it shows', async () => {
    const { fixture, c, http } = await setup({ gameId: 7, page: 2 });
    fixture.detectChanges();
    http.expectOne('/api/games/7/photo?page=2').flush(blob(2), { headers: { 'X-Page-Count': '3' } });
    expect(c.page()).toBe(2);
    expect(c.pageCount()).toBe(3);
  });

  it('without the header it is one page', async () => {
    const { fixture, c, http } = await setup({ gameId: 9 });
    fixture.detectChanges();
    http.expectOne('/api/games/9/photo').flush(blob(1));
    fixture.detectChanges();
    expect(c.pageCount()).toBe(1);
    expect(fixture.nativeElement.querySelector('.pager')).toBeNull();
  });
});

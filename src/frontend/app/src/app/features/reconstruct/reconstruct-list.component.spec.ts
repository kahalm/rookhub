import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter, Router } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { ReconstructListComponent } from './reconstruct-list.component';
import { clickCenter, clickFreeSpot, expectWholeTileClickable, recordNavigation } from '../../testing/tile-hit';

describe('ReconstructListComponent', () => {
  let http: HttpTestingController;

  function create(): ReconstructListComponent {
    const fixture = TestBed.createComponent(ReconstructListComponent);
    fixture.detectChanges();
    return fixture.componentInstance;
  }

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ReconstructListComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
        provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }),
      ],
    }).compileComponents();
    http = TestBed.inject(HttpTestingController);
  });

  it('lädt die Liste beim Öffnen', () => {
    const c = create();
    http.expectOne('/api/reconstructions').flush([
      { id: 1, title: 'Runde 3', partCount: 2, knownPlies: 12, gaps: 1, updatedAt: '2026-09-20T10:00:00Z' },
    ]);
    expect(c.items().length).toBe(1);
    expect(c.loading()).toBeFalse();
  });

  // Ganze Karte klickbar (DESIGN 5.2): die Fläche gehört dem Titel-Link; „Öffnen“ und Löschen bleiben eigene Ziele.
  it('Karte als Ganzes klickbar: Fläche → Rekonstruktion öffnen; Löschen löst nur sich selbst aus', () => {
    const fixture = TestBed.createComponent(ReconstructListComponent);
    fixture.detectChanges();
    http.expectOne('/api/reconstructions').flush([
      { id: 1, title: 'Runde 3 gegen Zirl', white: 'Anna', black: 'Bert', partCount: 2, knownPlies: 12, gaps: 1, updatedAt: '2026-09-20T10:00:00Z' },
    ]);
    fixture.detectChanges();
    const went = recordNavigation();
    const remove = spyOn(fixture.componentInstance, 'remove');
    const card = (fixture.nativeElement as HTMLElement).querySelector<HTMLElement>('mat-card.item')!;
    const title = card.querySelector<HTMLElement>('a.item-title')!;
    expectWholeTileClickable(card, title);
    clickFreeSpot(card, title);
    clickCenter(card.querySelector('.chips .chip')!);
    clickCenter(card.querySelector('.actions a')!);
    expect(went()).toEqual(['/reconstruct/1', '/reconstruct/1', '/reconstruct/1']);
    clickCenter(card.querySelector('.actions button')!);
    expect(remove).toHaveBeenCalledTimes(1);
    expect(went().length).toBe(3);
  });

  // d2-rk-recon-list-lead: am Handy brach „Anlegen" in eine eigene Zeile — dort steht es als ＋ im Titelfeld.
  it('bietet „Anlegen" auch als ＋ im Titelfeld an und hält den Erklärtext kurz', () => {
    const fixture = TestBed.createComponent(ReconstructListComponent);
    fixture.detectChanges();
    http.expectOne('/api/reconstructions').flush([]);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const suffix = el.querySelector('.new-card mat-form-field button.create-suffix') as HTMLButtonElement;
    expect(suffix).not.toBeNull();
    expect(suffix.disabled).toBeTrue();
    // z2-rk-recon-anlegen-plus: das Feld sagt, dass hier eine neue Rekonstruktion entsteht.
    expect(el.querySelector('.new-card mat-label')?.textContent).toContain('reconstruct.createLabel');
    expect(el.querySelector('.header app-help-hint button')?.getAttribute('aria-label')).toBe('reconstruct.introMore');
  });

  it('springt nach dem Anlegen in die neue Rekonstruktion', () => {
    const c = create();
    http.expectOne('/api/reconstructions').flush([]);
    const router = TestBed.inject(Router);
    const nav = spyOn(router, 'navigate');

    c.newTitle = '  Runde 4  ';
    c.create();
    const req = http.expectOne({ url: '/api/reconstructions', method: 'POST' });
    expect(req.request.body).toEqual({ title: 'Runde 4' });   // getrimmt
    req.flush({ id: 9 });

    expect(nav).toHaveBeenCalledWith(['/reconstruct', 9]);
    expect(c.newTitle).toBe('');
  });

  it('ein leerer Titel löst gar keine Anfrage aus', () => {
    const c = create();
    http.expectOne('/api/reconstructions').flush([]);
    c.newTitle = '   ';
    c.create();
    http.expectNone({ url: '/api/reconstructions', method: 'POST' });
  });
});

/** a1-rk-recon-titel-linkfarbe: der Kartentitel ist ein Link, aber in Textfarbe — Akzent erst bei Hover/Fokus. */
describe('ReconstructListComponent Kartentitel', () => {
  const css = ((ReconstructListComponent as any).ɵcmp.styles as string[]).join('\n').replace(/%NS%/g, '').replace(/\s+/g, '')
    .replace(/\[_ngcontent-%COMP%\]/g, '');
  it('erbt die Textfarbe und zeigt Hover/Fokus in --mat-sys-primary', () => {
    expect(css).toMatch(/\.item-title\{color:inherit;/);
    expect(css).toMatch(/\.item-title:hover,\.item-title:focus-visible\{color:var\(--mat-sys-primary\);text-decoration:underline;?\}/);
  });
});

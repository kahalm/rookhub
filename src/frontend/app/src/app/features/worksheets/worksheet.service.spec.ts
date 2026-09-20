import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Router } from '@angular/router';
import { TranslateService } from '@ngx-translate/core';
import { of } from 'rxjs';
import { SnackbarService } from '../../core/snackbar.service';
import { WorksheetService } from './worksheet.service';

describe('WorksheetService', () => {
  let svc: WorksheetService;
  let http: HttpTestingController;
  let router: { navigate: jasmine.Spy };
  let snackbar: any;
  let actionRef: { onAction: () => any };

  const summary = (over: Partial<any> = {}) => ({
    id: 1, name: '', isClipboard: true, perPage: 6, itemCount: 0, shareToken: null,
    createdAt: '', updatedAt: '', ...over,
  });

  beforeEach(() => {
    router = { navigate: jasmine.createSpy('navigate') };
    actionRef = { onAction: () => of(undefined) };
    snackbar = {
      show: jasmine.createSpy('show').and.returnValue(actionRef),
      info: jasmine.createSpy('info'),
      warn: jasmine.createSpy('warn'),
    };
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(), provideHttpClientTesting(),
        { provide: Router, useValue: router },
        { provide: SnackbarService, useValue: snackbar },
        { provide: TranslateService, useValue: { instant: (k: string) => k } },
      ],
    });
    svc = TestBed.inject(WorksheetService);
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());

  it('die Zielliste des Sende-Menüs wird einmal geladen, nicht bei jedem Aufklappen', () => {
    svc.list().subscribe();
    http.expectOne('/api/worksheets').flush([summary()]);

    let out: any[] = [];
    svc.targetList().subscribe(l => out = l);
    expect(out.length).toBe(1);   // kein zweiter Request — http.verify() würde ihn sonst melden
  });

  it('nach dem Senden wird die Zielliste neu geholt (Anzahl hat sich geändert)', () => {
    svc.list().subscribe();
    http.expectOne('/api/worksheets').flush([summary()]);

    svc.addItems(null, [{ fen: 'f', orientation: 'white' }]).subscribe();
    http.expectOne('/api/worksheets/items').flush({ worksheetId: 1, name: '', isClipboard: true, added: 1, skipped: 0, total: 1, full: false });

    svc.targetList().subscribe();
    http.expectOne('/api/worksheets').flush([summary({ itemCount: 1 })]);
  });

  it('ohne Ziel geht das Senden in die Zwischenablage (worksheetId null)', () => {
    svc.addItems(null, [{ fen: 'f', orientation: 'black' }]).subscribe();
    const req = http.expectOne('/api/worksheets/items');
    expect(req.request.method).toBe('POST');
    expect(req.request.body.worksheetId).toBeNull();
    expect(req.request.body.items[0].orientation).toBe('black');
    req.flush({ worksheetId: 1, name: '', isClipboard: true, added: 1, skipped: 0, total: 1, full: false });
  });

  it('nichts Druckbares in der Auswahl: nur ein Hinweis, kein Serveraufruf', () => {
    svc.sendAndNotify(null, []);
    expect(snackbar.info).toHaveBeenCalledWith('worksheets.send.nothing');
  });

  it('nach dem Senden führt „Öffnen“ in der Snackbar direkt ins Blatt', () => {
    svc.sendAndNotify(null, [{ fen: 'f', orientation: 'white' }]);
    http.expectOne('/api/worksheets/items')
      .flush({ worksheetId: 5, name: '', isClipboard: true, added: 1, skipped: 0, total: 1, full: false });

    expect(snackbar.show).toHaveBeenCalled();
    expect(router.navigate).toHaveBeenCalledWith(['/worksheets', 5]);
  });

  it('ein volles Blatt meldet sich, statt die Stellungen stillschweigend zu schlucken', () => {
    svc.sendAndNotify(3, [{ fen: 'f', orientation: 'white' }]);
    http.expectOne('/api/worksheets/items')
      .flush({ worksheetId: 3, name: 'Mittwoch', isClipboard: false, added: 0, skipped: 0, total: 240, full: true });

    expect(snackbar.show).toHaveBeenCalledWith('worksheets.send.full', jasmine.anything());
  });

  it('Teilen gibt das Token des öffentlichen Links zurück', () => {
    let token: string | null = null;
    svc.share(4).subscribe(t => token = t);
    const req = http.expectOne('/api/worksheets/4/share');
    expect(req.request.method).toBe('POST');
    req.flush({ shareToken: 'Ux7f2K' });
    expect(token).toBe('Ux7f2K' as any);
  });

  it('das geteilte Blatt wird ohne Anmeldung über das Token geholt', () => {
    svc.getShared('Ux7f2K').subscribe();
    http.expectOne('/api/worksheets/shared/Ux7f2K').flush({ name: 'Mittwoch', items: [] });
  });

  it('die Adresse im QR-Code zeigt auf diese Installation', () => {
    expect(svc.shareUrl('Ux7f2K')).toBe(`${window.location.origin}/w/Ux7f2K`);
  });

  it('Teilen beenden löscht den Link', () => {
    svc.unshare(4).subscribe();
    const req = http.expectOne('/api/worksheets/4/share');
    expect(req.request.method).toBe('DELETE');
    req.flush(null);
  });

  it('Umsortieren schickt die IDs in Wunsch-Reihenfolge', () => {
    svc.reorder(2, [9, 3, 7]).subscribe();
    const req = http.expectOne('/api/worksheets/2/order');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body.itemIds).toEqual([9, 3, 7]);
    req.flush({});
  });
});

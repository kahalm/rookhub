import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ScanExport } from './scan-export';
import { ScanExportActionsComponent } from './scan-export-actions.component';

describe('ScanExportActionsComponent', () => {
  let fixture: ComponentFixture<ScanExportActionsComponent>;
  let exp: ScanExport;

  function create(loggedIn: boolean, rookHub: string | null): HTMLElement {
    exp = new ScanExport({ input: () => ({ moves: [], white: null, black: null, result: '*', event: null, year: null }),
      addToMyGames: async () => ({ imported: 1, ids: [5] }), download: () => undefined });
    TestBed.configureTestingModule({ imports: [ScanExportActionsComponent] });
    fixture = TestBed.createComponent(ScanExportActionsComponent);
    fixture.componentRef.setInput('exp', exp);
    fixture.componentRef.setInput('loggedIn', loggedIn);
    fixture.componentRef.setInput('rookHub', rookHub);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('ohne Anmeldung kein „Zu meinen Partien"', () => {
    const el = create(false, null);
    expect(el.textContent).toContain('PGN herunterladen');
    expect(el.textContent).not.toContain('Zu meinen Partien');
  });

  it('angemeldet: nach dem Hinzufügen gesperrt, mit Link nach RookHub', async () => {
    const el = create(true, 'https://rookhub.example');
    const add = Array.from(el.querySelectorAll('button')).find(b => b.textContent?.includes('Zu meinen Partien')) as HTMLButtonElement;
    add.click();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(add.disabled).toBeTrue();
    expect(el.querySelector('a')?.getAttribute('href')).toBe('https://rookhub.example/games/5');
    expect(el.querySelector('.update-msg')?.textContent).toContain('In deinen Partien gespeichert.');
  });
});

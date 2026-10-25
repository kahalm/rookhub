import { ComponentFixture, TestBed, fakeAsync, flush, flushMicrotasks } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { ClubClient } from '../../core/club-api.service';
import { ScoresheetUploadStore } from './scoresheet-upload-store';
import { ClubScoresheetPanelComponent } from './club-scoresheet-panel.component';

describe('ClubScoresheetPanelComponent', () => {
  let fixture: ComponentFixture<ClubScoresheetPanelComponent>;
  let api: jasmine.SpyObj<ClubClient>;
  let store: ScoresheetUploadStore;

  beforeEach(() => {
    api = jasmine.createSpyObj<ClubClient>('ClubClient', ['scans', 'scoresheetStatus', 'upload', 'discard']);
    api.scans.and.resolveTo([]);
    api.scoresheetStatus.and.resolveTo({ available: true, dailyLimit: 10, usedToday: 0, languages: [{ code: 'de', name: 'Deutsch', pieces: 'KDTLS' }] });
    store = new ScoresheetUploadStore({ client: api, share: null, isManager: false, openScans: async () => [],
      confirm: async () => true, kind: () => 'formular' });
    TestBed.configureTestingModule({ imports: [ClubScoresheetPanelComponent], providers: [provideRouter([])] });
    fixture = TestBed.createComponent(ClubScoresheetPanelComponent);
    fixture.componentRef.setInput('store', store);
  });
  afterEach(() => store.dispose());

  it('ohne Status „Lade …", danach Fotofeld; hochladen leert das Feld', fakeAsync(() => {
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.textContent).toContain('Lade …');
    void store.loadStatus();
    flushMicrotasks();
    fixture.detectChanges();
    expect(el.textContent).toContain('Anthropic (USA)');
    const input = el.querySelector('input[type=file][accept^="image"]') as HTMLInputElement;
    const dt = new DataTransfer();
    dt.items.add(new File(['x'], 'bogen.jpg', { type: 'image/jpeg' }));
    input.files = dt.files;
    input.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    expect(el.querySelector('.chosen')?.textContent).toContain('bogen.jpg');
    api.upload.and.resolveTo({ ref: '7', scan: { id: 7, status: 'pending', notationLanguage: 'de', createdAt: new Date().toISOString(),
      rounds: 0, moveCount: 0, uncertainCount: 0, unresolvedCount: 0 } });
    (el.querySelector('.actions .btn-pri') as HTMLButtonElement).click();
    flushMicrotasks();
    fixture.detectChanges();
    expect(input.value).toBe('');
    expect(el.querySelector('.scan-list .scan-clock')).not.toBeNull();             // wird gelesen: Uhr läuft
    store.dispose();
    flush();
  }));
});

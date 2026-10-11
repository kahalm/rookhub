import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { ClubApiService } from '../core/club-api.service';
import { LinkStateStore } from '../core/link-state.store';
import { ClubGateComponent } from './club-gate.component';

@Component({ standalone: true, imports: [ClubGateComponent], template: `<ch-gate [start]="start" area="Die Gruppen sehen nur Trainer und die Vereinsleitung." />` })
class HostComponent { start = true; }

/** UI-Sweep 2026-10-11 (r2-c-gate-deadend): eine Sperrkarte mit Weg zur Startseite und zum Verknüpfen. */
describe('ClubGateComponent', () => {
  let api: jasmine.SpyObj<ClubApiService>;

  async function create(start = true): Promise<HTMLElement> {
    TestBed.configureTestingModule({
      imports: [HostComponent],
      providers: [provideRouter([]), { provide: ClubApiService, useValue: api }],
    });
    const f = TestBed.createComponent(HostComponent);
    f.componentInstance.start = start;
    f.detectChanges();
    await f.whenStable();
    f.detectChanges();
    return f.nativeElement as HTMLElement;
  }

  beforeEach(() => {
    api = jasmine.createSpyObj<ClubApiService>('ClubApiService', ['linkState']);
    api.linkState.and.resolveTo({ linked: false });
  });

  it('nicht verknüpft: Text der Seite, „Zur Startseite" und „Konto verknüpfen"', async () => {
    const el = await create();
    expect(el.textContent).toContain('Nicht freigeschaltet');
    expect(el.textContent).toContain('Die Gruppen sehen nur Trainer und die Vereinsleitung.');
    expect(el.querySelector('a[href="/"]')!.textContent).toContain('Zur Startseite');
    expect(el.querySelector('a[href="/verknuepfen"]')!.textContent).toContain('Konto verknüpfen');
  });

  it('verknüpft: Auskunft über die Verknüpfung statt der Code-Aufforderung', async () => {
    api.linkState.and.resolveTo({ linked: true, firstName: 'Lena' });
    const el = await create();
    expect(el.textContent).toContain('Hallo Lena!');
    expect(el.textContent).not.toContain('Nicht freigeschaltet');
    expect(el.querySelector('a[href="/verknuepfen"]')!.textContent).toContain('Verknüpfung ansehen');
    expect(el.querySelector('a[href="/"]')).not.toBeNull();
  });

  it('auf der Startseite selbst ohne „Zur Startseite"', async () => {
    const el = await create(false);
    expect(el.querySelector('a[href="/"]')).toBeNull();
    expect(el.querySelector('a[href="/verknuepfen"]')).not.toBeNull();
  });

  it('der Stand wird nur einmal geholt, auch für mehrere Karten', async () => {
    await create();
    const store = TestBed.inject(LinkStateStore);
    await store.ensure();
    expect(api.linkState).toHaveBeenCalledTimes(1);
  });
});

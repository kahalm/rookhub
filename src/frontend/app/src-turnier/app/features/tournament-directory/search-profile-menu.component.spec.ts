import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { SearchProfileMenuComponent } from './search-profile-menu.component';
import { SearchProfile } from './tournament-directory.model';

function profile(id: number, name: string): SearchProfile {
  return {
    id, name, placeQuery: null, lat: 47.27, lon: 11.39, radiusKm: 50, federations: [], speeds: [],
    weekendOnly: false, minPlayers: null, notifyNew: false, sortOrder: id,
  };
}

describe('SearchProfileMenuComponent', () => {
  let fixture: ComponentFixture<SearchProfileMenuComponent>;
  let events: string[];

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [SearchProfileMenuComponent],
      providers: [provideTranslateService({ fallbackLang: 'en' }), provideNoopAnimations()],
    }).compileComponents();
    fixture = TestBed.createComponent(SearchProfileMenuComponent);
    events = [];
    const c = fixture.componentInstance;
    c.pick.subscribe(id => events.push(`pick:${id}`));
    c.create.subscribe(() => events.push('create'));
    c.edit.subscribe(p => events.push(`edit:${p.id}`));
    c.remove.subscribe(p => events.push(`remove:${p.id}`));
  });

  afterEach(() => document.querySelectorAll('.cdk-overlay-container').forEach(n => n.replaceChildren()));

  const items = () => [...document.querySelectorAll<HTMLButtonElement>('.mat-mdc-menu-panel [mat-menu-item]')];
  const texts = () => items().map(i => (i.querySelector('.mat-mdc-menu-item-text') ?? i).textContent!.replace(/\s+/g, ' ').trim());
  const open = async () => {
    fixture.nativeElement.querySelector('button[aria-label="tournamentDirectory.profile.manage"]').click();
    fixture.detectChanges();
    await fixture.whenStable();
  };

  it('bietet ohne Profile nur „Neues Suchprofil"', async () => {
    fixture.detectChanges();
    await open();

    expect(texts()).toEqual(['tournamentDirectory.profile.new']);
    items()[0].click();
    expect(events).toEqual(['create']);
  });

  it('bietet mit gewähltem Profil Auswahl, Bearbeiten und Löschen', async () => {
    const tirol = profile(3, 'Tirol');
    fixture.componentRef.setInput('profiles', [tirol, profile(4, 'Wien')]);
    fixture.componentRef.setInput('activeProfile', tirol);
    fixture.detectChanges();
    await open();

    expect(texts()).toEqual([
      'tournamentDirectory.profile.label', 'tournamentDirectory.profile.new',
      'common.edit', 'tournamentDirectory.profile.delete',
    ]);
    items()[2].click();
    await open();
    items()[3].click();
    expect(events).toEqual(['edit:3', 'remove:3']);
  });

  it('wählt im Untermenü ein Profil oder „kein Profil"', async () => {
    fixture.componentRef.setInput('profiles', [profile(3, 'Tirol'), profile(4, 'Wien')]);
    fixture.detectChanges();
    await open();
    items()[0].click();                         // Untermenü öffnen
    fixture.detectChanges();
    await fixture.whenStable();

    const sub = texts().slice(2);
    expect(sub).toEqual(['tournamentDirectory.profile.none', 'Tirol · 50 km', 'Wien · 50 km']);
    items()[4].click();
    expect(events).toEqual(['pick:4']);
  });
});

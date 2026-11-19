import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { AuthService } from '@rh/core/auth.service';
import { clickCenter, clickFreeSpot, expectWholeTileClickable, recordNavigation } from '@rh/testing/tile-hit';
import { ClubApiService } from '../../core/club-api.service';
import { GroupRow } from '../../core/club.models';
import { GroupsPageComponent } from './groups-page.component';

const GROUP = (id: number, name: string, extra: Partial<GroupRow> = {}): GroupRow =>
  ({ id, name, weekday: 5, schedule: '17:00 Vereinsheim', archived: false, memberCount: 7, sessionCount: 3, lastSession: '2026-11-13',
    trainers: [{ userId: 2, username: 'trainer' }], ...extra });

describe('GroupsPageComponent (Gruppen)', () => {
  async function create(groups: GroupRow[]) {
    const api = jasmine.createSpyObj<ClubApiService>('ClubApiService', ['groups']);
    api.groups.and.resolveTo(groups);
    TestBed.configureTestingModule({
      imports: [GroupsPageComponent],
      providers: [provideRouter([]), { provide: ClubApiService, useValue: api },
        { provide: AuthService, useValue: { has: (p: string) => p === 'club.trainer' } }],
    });
    const fixture = TestBed.createComponent(GroupsPageComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture;
  }

  // Ganze Karte klickbar (DESIGN 5.2): die Fläche gehört der Gruppenseite (Name), „Anwesenheit abhaken“ bleibt eigenes Ziel.
  it('Gruppenkarte als Ganzes klickbar: Fläche → Gruppenseite; „Anwesenheit abhaken“ führt nur zur Anwesenheit', async () => {
    const fixture = await create([GROUP(4, 'Anfänger am Freitag'), GROUP(5, 'Ehemalige', { archived: true })]);
    const went = recordNavigation();
    const cards = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll<HTMLElement>('li.group'));
    expect(cards.length).toBe(2);
    for (const card of cards) expectWholeTileClickable(card, card.querySelector('h2 a')!);
    const name = cards[0].querySelector<HTMLElement>('h2 a')!;
    expect(name.getAttribute('href')).toBe('/gruppen/4');
    clickFreeSpot(cards[0], name);
    clickCenter(cards[0].querySelector('.kid-meta span')!);
    clickCenter(cards[0].querySelector('a.btn')!);
    expect(went()).toEqual(['/gruppen/4', '/gruppen/4', '/gruppen/4/anwesenheit']);
    // Archivierte Gruppe: kein Anwesenheits-Knopf, die Karte führt weiter zur Gruppenseite.
    expect(cards[1].querySelector('a.btn')).toBeNull();
    clickFreeSpot(cards[1], cards[1].querySelector('h2 a')!);
    expect(went()[3]).toBe('/gruppen/5');
  });
});

import { Provider, computed, signal } from '@angular/core';
import { of } from 'rxjs';
import { ClubContextService, LeagueClubInfo } from './club-context.service';

/** Der Verein der Specs (0.698.0): bewusst nicht Schwaz — die Seiten sollen nichts mehr an einem festen Namen festmachen. */
export const TEST_CLUB: LeagueClubInfo = { id: 1, name: 'SK Testdorf', anonName: 'Testdorf', teamPrefix: 'Testdorf', source: null };

/** Ein {@link ClubContextService} ohne Server: ein fester Verein (oder mehrere), `ensure()` sofort. */
export function provideTestClub(club: LeagueClubInfo | null = TEST_CLUB, clubs: LeagueClubInfo[] = club ? [club] : []): Provider {
  const current = signal<LeagueClubInfo | null>(club);
  const shareClub = signal<LeagueClubInfo | null>(null);
  const active = computed(() => shareClub() ?? current());
  const stub: Partial<ClubContextService> = {
    clubs: signal(clubs),
    current,
    shareClub,
    loaded: signal(true),
    club: active,
    anonName: computed(() => active()?.anonName ?? 'Verein'),
    clubName: computed(() => active()?.name ?? 'dem Verein'),
    ensure: () => of(current()?.id ?? null),
    select: (id: number) => current.set(clubs.find(c => c.id === id) ?? current()),
    useShareClub: (c: LeagueClubInfo | null | undefined) => shareClub.set(c ?? null),
    useShare: () => { /* der Verein des Links: im Test wie gesetzt */ },
  };
  return { provide: ClubContextService, useValue: stub };
}

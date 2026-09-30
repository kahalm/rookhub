import { AuthService } from '@rh/core/auth.service';

/** Darf dieses Konto ClubHub sehen? Leitung (`club.manage`, Admin eingeschlossen) oder Trainer (`club.trainer`). */
export function hasClubAccess(auth: Pick<AuthService, 'has'>): boolean {
  return auth.has('club.manage') || auth.has('club.trainer');
}

/** Gruppen anlegen, Trainer zuteilen, Blätter löschen: nur die Leitung. */
export function isClubManager(auth: Pick<AuthService, 'has'>): boolean {
  return auth.has('club.manage');
}

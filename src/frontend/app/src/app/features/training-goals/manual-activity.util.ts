import { ManualActivityKind } from './training-goals.service';

/** Alle manuellen Aktivitätsarten + ob sie in Minuten (sonst Partienzahl) gemessen werden. */
export const MANUAL_KINDS: { kind: ManualActivityKind; minutes: boolean }[] = [
  { kind: 'OtbGame', minutes: false },
  { kind: 'OfflinePuzzle', minutes: true },
  { kind: 'OfflineStudy', minutes: true },
  { kind: 'Coaching', minutes: true },
];

/** Wird die Art in Minuten gemessen (sonst Anzahl Partien)? */
export function isMinutesKind(kind: ManualActivityKind): boolean {
  return kind !== 'OtbGame';
}

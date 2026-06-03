/**
 * Spiegelt das Backend-Enum {@link RookHub.Api.Models.RepertoireKind}.
 * Reihenfolge MUSS mit dem .NET-Enum übereinstimmen (None=0).
 */
export enum RepertoireKind {
  None = 0,
  Opening = 1,
  Middlegame = 2,
  Endgame = 3,
}

/** i18n-Schlüssel je Kind — wird in Chips/Dropdowns wiederverwendet. */
export const REPERTOIRE_KIND_LABELS: Record<RepertoireKind, string> = {
  [RepertoireKind.None]: 'repertoire.kind.none',
  [RepertoireKind.Opening]: 'repertoire.kind.opening',
  [RepertoireKind.Middlegame]: 'repertoire.kind.middlegame',
  [RepertoireKind.Endgame]: 'repertoire.kind.endgame',
};

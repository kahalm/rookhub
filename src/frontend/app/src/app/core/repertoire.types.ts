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

/**
 * UI-Sweep 2026-10-11 (r2-rg-rep-cards): die API serialisiert Enums als NAMEN („None", „Opening", …,
 * JsonStringEnumConverter) — der Vergleich `rep.kind !== RepertoireKind.None` war deshalb immer wahr, und jede Karte
 * trug einen „–"-Chip (die Kategorie-Farben griffen nie). Nimmt Zahl oder Namen, liefert das Enum (unbekannt → None).
 */
export function toRepertoireKind(value: RepertoireKind | string | number | null | undefined): RepertoireKind {
  if (typeof value === 'number') return value in RepertoireKind ? value as RepertoireKind : RepertoireKind.None;
  if (typeof value === 'string') {
    const byName = (RepertoireKind as unknown as Record<string, RepertoireKind>)[value];
    if (typeof byName === 'number') return byName;
    const n = Number(value);
    return Number.isInteger(n) && n in RepertoireKind ? n as RepertoireKind : RepertoireKind.None;
  }
  return RepertoireKind.None;
}

import { TrainingSpeakerTurn } from './training-videos.service';

/** Bekannte Sprecher-Kennungen → Anzeigename (Eigennamen, nicht übersetzt). Unbekannte: erster Buchstabe groß. */
const SPEAKER_NAMES: Record<string, string> = { sasa: 'Saša', patrik: 'Patrik' };

export function speakerName(sp: string | null | undefined): string {
  if (!sp) return '';
  return SPEAKER_NAMES[sp] ?? sp.charAt(0).toUpperCase() + sp.slice(1);
}

/** Wechsel nach Zeit (die API liefert sie geordnet; hier nur zur Sicherheit). */
export function sortedTurns(turns: TrainingSpeakerTurn[] | null | undefined): TrainingSpeakerTurn[] {
  return [...(turns ?? [])].filter(t => Number.isFinite(t.t) && t.t >= 0).sort((a, b) => a.t - b.t);
}

/** Index des Beitrags, der bei `t` läuft (-1 = vor dem ersten). */
export function turnIndexAt(turns: TrainingSpeakerTurn[], t: number): number {
  let lo = 0, hi = turns.length - 1, hit = -1;
  while (lo <= hi) {
    const mid = (lo + hi) >> 1;
    if (turns[mid].t <= t) { hit = mid; lo = mid + 1; } else hi = mid - 1;
  }
  return hit;
}

/** Nächster Wechsel nach `t` (kleiner Vorlauf, damit ein Sprung genau auf einen Wechsel nicht dort hängen bleibt). */
export function nextTurn(turns: TrainingSpeakerTurn[], t: number): TrainingSpeakerTurn | null {
  return turns[turnIndexAt(turns, t + 0.5) + 1] ?? null;
}

/** Sekunden, in denen „zurück“ zum VORIGEN Beitrag springt statt an den Anfang des laufenden (wie ⏮ in Playern). */
export const RESTART_GRACE_SEC = 3;

/** „Zurück“: Anfang des laufenden Beitrags; läuft er erst seit < 3 s, der Anfang des vorigen. */
export function previousTurn(turns: TrainingSpeakerTurn[], t: number): TrainingSpeakerTurn | null {
  const i = turnIndexAt(turns, t);
  if (i < 0) return null;
  if (t - turns[i].t < RESTART_GRACE_SEC && i > 0) return turns[i - 1];
  return turns[i];
}

/** Ein Abschnitt der Sprecherleiste: Anteil in Prozent der Videolänge. */
export interface SpeakerBand { sp: string; startPct: number; widthPct: number }

export function speakerBands(turns: TrainingSpeakerTurn[], durationSec: number): SpeakerBand[] {
  if (!(durationSec > 0)) return [];
  const bands: SpeakerBand[] = [];
  for (let i = 0; i < turns.length; i++) {
    const start = Math.min(turns[i].t, durationSec);
    const end = Math.min(turns[i + 1]?.t ?? durationSec, durationSec);
    if (end <= start) continue;
    bands.push({ sp: turns[i].sp, startPct: (start / durationSec) * 100, widthPct: ((end - start) / durationSec) * 100 });
  }
  return bands;
}

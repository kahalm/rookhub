import { InjectionToken, inject } from '@angular/core';
import { LeagueApiService } from '@lh/core/league-api.service';
import { OpeningTree, PlayerCard, ProfileView, RecentGames, TreeFilter } from '@lh/core/league.models';
import { TrainingLines, TrainingLinesQuery, TrainingRepertoireResult } from './training-lines';

/**
 * Woher die Spielerkarte (und ihr Eröffnungsbaum) die Daten holt — die schmale Schnittstelle zwischen Karte und API
 * (Spielervorbereitung, Phase 3). LeagueHub: der `LeagueApiService` (die Vorgabe des Tokens — deshalb brauchen dessen
 * Seiten und Specs nichts); die Spielervorbereitung der Haupt-App stellt ihren eigenen Adapter bereit
 * (`features/prep/prep-card-api.ts`).
 *
 * `key` ist bei LeagueHub die FIDE-ID, sonst das, was die Karte als `key` mitbringt (`PlayerCard.key`); `token` gibt es
 * nur bei Teilen-Links der Liga.
 */
export interface PlayerCardApi {
  card(key: string, token: string | null): Promise<PlayerCard>;
  profile(key: string, token: string | null, filter: TreeFilter): Promise<ProfileView>;
  recent(key: string, token: string | null, color?: 'w' | 's'): Promise<RecentGames>;
  tree(key: string, color: 'w' | 's', line: string[], token: string | null, filter?: TreeFilter): Promise<OpeningTree>;
  pgn(key: string, token: string | null): Promise<Blob>;
  /** Online-Konten pflegen (anlegen, ändern, Vorschläge) — nur LeagueHub. Fehlt = ja (Recht `league.manage` entscheidet). */
  readonly accountsEditable?: boolean;
  /** Den Schalter „auch unsichere Konten" anbieten. Fehlt = ja (angemeldet bei LeagueHub immer). */
  unsureAllowed?(): boolean;
  /** Trainingslinien gegen diesen Spieler (2026-10-07) — nur angemeldet, nie über einen Teilen-Link. Fehlt = kein Abschnitt. */
  trainingLines?(key: string, q: TrainingLinesQuery): Promise<TrainingLines>;
  /** „Show me lines to train": eigenes Repertoire „Prep: … Jahr" mit den bis zu 50 wichtigsten Linien anlegen bzw. ersetzen. */
  trainingRepertoire?(key: string, q: TrainingLinesQuery): Promise<TrainingRepertoireResult>;
  /** Wie der Trainer diesen Gegner findet (`?opponent=` und was sonst mitgeht). Fehlt = `league:<key>`. */
  trainerParams?(key: string): Record<string, string>;
}

export const PLAYER_CARD_API = new InjectionToken<PlayerCardApi>('PLAYER_CARD_API', {
  providedIn: 'root',
  factory: () => inject(LeagueApiService),
});

import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { LibraryGame } from '../guess/library.service';
import { GameAnalysis } from '../analysis/game-analysis.service';
import { GameEvals } from './game-review.util';

/** Listeneintrag einer gespeicherten Partie (ohne PGN). */
export interface SavedGame {
  id: number;
  source: string;
  white?: string | null;
  black?: string | null;
  result?: string | null;
  playedAt?: string | null;
  sourceUrl?: string | null;
  shareToken: string;
  moveCount: number;
  createdAt: string;
  /** Wertung der Seiten (0.526.0, eigene Spalten); `null` = keine bekannt. */
  whiteElo?: number | null;
  blackElo?: number | null;
  /** Bedenkzeit in PGN-Schreibweise („180+2"); `null` = unbekannt (Altbestand). */
  timeControl?: string | null;
  /** Stand der verknüpften Analyse (0.515.0); `null` = keine — die Liste zeigt dann den Analysieren-Knopf. */
  analysis?: SavedGameAnalysis | null;
  /** Stand des Fehler-Trainings (0.524.0); `null` = noch nie trainiert. */
  mistakes?: GameMistakeProgress | null;
  /** Die Formular-Einlesung, aus der die Partie stammt (0.529.0); `null` = kein Foto. */
  scanId?: number | null;
}

/** Kopf der verknüpften Analyse für die Partienliste: Fortschritt, und wenn fertig die Genauigkeit je Seite. */
export interface SavedGameAnalysis {
  status: 'pending' | 'running' | 'done' | 'failed';
  analyzed: number;
  total: number;
  /** Prozent nach der Lichess-Formel (Server-Spiegel von `game-review.util`); `null` = kein bewertbarer Zug. */
  accuracyWhite?: number | null;
  accuracyBlack?: number | null;
}

/** Wie viele Fehler einer Partie schon selbst gefunden sind — Quelle der Anzeige „4 von 7 · 3 offen". */
export interface GameMistakeProgress {
  total: number;
  solved: number;
  open: number;
  solvedPlies: number[];
  lastTrainedAt: string;
}

/** Detail inkl. PGN (zum Nachspielen/Analysieren). */
export interface SavedGameDetail extends SavedGame {
  pgn: string;
  /** "white"/"black", wenn der Besitzer einer Seite zuordenbar ist — initiale Brett-Orientierung. */
  ownerSide?: 'white' | 'black' | null;
}

/** Öffentliche Sicht auf eine geteilte Partie (ohne Besitzer-Daten). */
export interface SharedGame {
  source: string;
  white?: string | null;
  black?: string | null;
  result?: string | null;
  playedAt?: string | null;
  sourceUrl?: string | null;
  pgn: string;
  createdAt: string;
  whiteElo?: number | null;
  blackElo?: number | null;
  /** "white"/"black", wenn der Teilende einer Seite zuordenbar ist — initiale Brett-Orientierung. */
  ownerSide?: 'white' | 'black' | null;
  /** Nur für den angemeldeten BESITZER: die Id seiner Partie — die Seite wechselt dann auf `/games/{id}`. */
  ownGameId?: number | null;
  /** „Kurz erzählt" (0.541.0): die Partie in zwei, drei Sätzen — dieselbe Zeile steht in der Link-Vorschau. */
  recap?: string | null;
}

/** Antwort auf „Partie analysieren" (`POST …/analyze`): neu eingereiht oder wiederverwendet. */
export interface GameAnalyzeResult {
  analysis: GameAnalysis | null;
  reason?: string | null;
  /** Nichts neu eingereiht — die Partie war schon (oder wird gerade) gerechnet. */
  reused: boolean;
}

/** Eine Erklärung zu einem Fehler (Halbzug 0-basiert wie in den Bewertungen). */
export interface GameExplanation {
  ply: number;
  class: string;
  text: string;
  /** Der Meisterkommentar zu DIESER Stellung, mit dem die Erklärung geschrieben wurde (0.542.0). */
  master?: GameExplanationMaster | null;
}

/** Quelle und Wortlaut eines Meisterkommentars aus dem Rohbestand. */
export interface GameExplanationMaster {
  libraryGameId: number;
  white?: string | null;
  black?: string | null;
  event?: string | null;
  year?: number | null;
  annotator?: string | null;
  text: string;
}

/** „Warum war das ein Fehler?" (0.534.0) — die Erklärungen einer Partie in einer Sprache. */
export interface GameExplanations {
  /** Ein Sprachmodell auf eigener Hardware ist eingerichtet. */
  available: boolean;
  /** Der Aufrufer darf erzeugen lassen (Besitzer, Analyse fertig, nichts läuft). */
  canGenerate: boolean;
  running: boolean;
  /** Sperrzeit der Spark (0.546.0): bis dahin entsteht nichts (nur für den Besitzer gesetzt). */
  quietUntil?: string | null;
  language: string;
  items: GameExplanation[];
}

/** „Roast my game" (0.535.0): die drei Stile — freundlich, frech, russisch (gnadenlos). */
export type RoastStyle = 'friendly' | 'cheeky' | 'russian';

export interface GameRoast {
  style: RoastStyle;
  language: string;
  text: string;
  createdAt: string;
}

export interface GameRoasts {
  /** Ein Sprachmodell auf eigener Hardware ist eingerichtet. */
  available: boolean;
  /** Die Partie hat eine fertige Analyse — ohne sie gibt es nichts zu roasten. */
  hasAnalysis: boolean;
  /** Sperrzeit der Spark (0.546.0): bis dahin wird nicht gewürfelt. */
  quietUntil?: string | null;
  items: GameRoast[];
}

/** „Kurz erzählt" (0.541.0) — die Nacherzählung einer eigenen Partie. */
export interface GameRecap {
  /** Ein Sprachmodell auf eigener Hardware ist eingerichtet. */
  available: boolean;
  /** Die Partie hat eine fertige Analyse — ohne sie gibt es nichts zu erzählen. */
  hasAnalysis: boolean;
  text?: string | null;
  language?: string | null;
  /** Fehlt noch, entsteht aber gerade (von diesem Aufruf angestoßen) — später nachfragen. */
  pending: boolean;
  /** Sperrzeit der Spark (0.546.0): der fehlende Text entsteht erst danach, beim nächsten Öffnen. */
  quietUntil?: string | null;
}

/** „Ähnliche Meisterpartien" (0.544.0): Partien des Rohbestands mit der längsten gemeinsamen Zugfolge. */
export interface SimilarGame {
  game: LibraryGame;
  sharedPlies: number;
  /** Der letzte gemeinsame Zug („3...dxe4"). */
  lastSharedMove?: string | null;
  /** Der Zug des Meisters an der Abzweigung („4.Nc3"); fehlt, wenn seine Zeile dort endet. */
  masterMove?: string | null;
  /** Der Zug dieser Partie an derselben Stelle. */
  gameMove?: string | null;
}

export interface SimilarGames {
  opening?: string | null;
  sharedPlies: number;
  sharedLine?: string | null;
  items: SimilarGame[];
}

@Injectable({ providedIn: 'root' })
export class GamesService {
  constructor(private http: HttpClient) {}

  list(take = 200): Observable<SavedGame[]> {
    return this.http.get<SavedGame[]>(`/api/games?take=${take}`);
  }

  get(id: number): Observable<SavedGameDetail> {
    return this.http.get<SavedGameDetail>(`/api/games/${id}`);
  }

  delete(id: number): Observable<void> {
    return this.http.delete<void>(`/api/games/${id}`);
  }

  getShared(token: string): Observable<SharedGame> {
    return this.http.get<SharedGame>(`/api/games/shared/${encodeURIComponent(token)}`);
  }

  // Die Adressen der Analyse stehen HIER an einer Stelle: Liste, Nachspiel-Dialog und geteilte
  // Seite reichen sie als fertige URL an Knopf und Kurve weiter, statt sie je dreimal zusammenzusetzen.

  /** „Partie analysieren" an einer eigenen Partie. */
  analyzeUrl(id: number): string { return `/api/games/${id}/analyze`; }

  /**
   * Fortschritt im Fehler-Training melden: Aufgabenzahl und die in diesem Durchlauf SELBST gefundenen
   * Halbzüge. Additiv und idempotent — zweimal dasselbe zu melden ändert nichts, und ein zweiter
   * Durchlauf nimmt nichts weg.
   */
  recordMistakes(id: number, total: number, solved: number[]): Observable<GameMistakeProgress> {
    return this.http.post<GameMistakeProgress>(`/api/games/${id}/mistakes`, { total, solved });
  }
  /** Bewertungen einer eigenen Partie (Nachspiel-Dialog). */
  evalsUrl(id: number): string { return `/api/games/${id}/evals`; }
  /** „Partie analysieren" auf der geteilten Partie — jeder Angemeldete. */
  sharedAnalyzeUrl(token: string): string { return `/api/games/shared/${encodeURIComponent(token)}/analyze`; }
  /** Bewertungen der geteilten Partie — auch ohne Anmeldung (dann nur die des Teilenden). */
  sharedEvalsUrl(token: string): string { return `/api/games/shared/${encodeURIComponent(token)}/evals`; }

  /** `lang` = Sprache der Seite: darin schreibt der Server nach der Analyse die Erklärungen und Roasts (0.540.0). */
  analyze(url: string, lang?: string): Observable<GameAnalyzeResult> {
    return this.http.post<GameAnalyzeResult>(url, lang ? { lang } : {});
  }

  evals(url: string): Observable<GameEvals> {
    return this.http.get<GameEvals>(url);
  }

  /** „Warum war das ein Fehler?" (0.534.0): die Erklärungen liegen neben den Bewertungen — dieselbe Adresse mit
   *  `/explanations` statt `/evals` (eigene Partie wie Teilen-Link). */
  explanationsUrl(evalsUrl: string): string { return evalsUrl.replace(/\/evals$/, '/explanations'); }

  explanations(url: string, lang: string): Observable<GameExplanations> {
    return this.http.get<GameExplanations>(url, { params: { lang } });
  }

  /** „Roast my game" (0.535.0): die gewürfelten Kommentare einer eigenen Partie. */
  roasts(id: number, lang: string): Observable<GameRoasts> {
    return this.http.get<GameRoasts>(`/api/games/${id}/roasts`, { params: { lang } });
  }

  /** Würfeln — ersetzt den vorigen Text desselben Stils. Dauert ein paar Sekunden (Sprachmodell auf der Spark). */
  roast(id: number, style: RoastStyle, lang: string): Observable<GameRoast> {
    return this.http.post<GameRoast>(`/api/games/${id}/roasts`, {}, { params: { style, lang } });
  }

  /** „Kurz erzählt" (0.541.0) der eigenen Partie. Fehlt sie bei fertiger Analyse, stößt schon der Abruf sie an. */
  recap(id: number): Observable<GameRecap> {
    return this.http.get<GameRecap>(`/api/games/${id}/recap`);
  }

  /** „Ähnliche Meisterpartien" (0.544.0) — eigene Partie bzw. Teilen-Link (auch ohne Anmeldung). */
  similarUrl(id: number): string { return `/api/games/${id}/similar`; }
  sharedSimilarUrl(token: string): string { return `/api/games/shared/${encodeURIComponent(token)}/similar`; }

  similar(url: string): Observable<SimilarGames> {
    return this.http.get<SimilarGames>(url);
  }

  /** Erzeugen anstoßen (nur der Besitzer; läuft im Hintergrund, der Client fragt nach). */
  requestExplanations(url: string, lang: string): Observable<GameExplanations> {
    return this.http.post<GameExplanations>(url, {}, { params: { lang } });
  }

  /** Absolute Teilen-URL einer Partie (für Copy-to-Clipboard). */
  shareUrl(shareToken: string): string {
    return `${window.location.origin}/g/${shareToken}`;
  }
}

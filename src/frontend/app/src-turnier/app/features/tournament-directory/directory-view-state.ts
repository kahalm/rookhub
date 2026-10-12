import { DestroyRef } from '@angular/core';
import { Subject, debounceTime, distinctUntilChanged, filter, switchMap } from 'rxjs';
import { ViewStateService } from '@rh/core/view-state.service';
import { PIN_COLOUR_SCHEMES, PinColourBy } from './pin-palette';
import { MapView } from './tournament-map.component';
import {
  DIRECTORY_AGE_GROUPS, DIRECTORY_GENDERS, DIRECTORY_KINDS, DIRECTORY_RANGE_PRESETS,
  DirectoryFilter, DirectoryRangePreset, TournamentSpeed } from './tournament-directory.model';

export type ViewTab = 'list' | 'map' | 'calendar';

/**
 * Die Felder des Turnierkalenders, die der gemerkte Anzeigezustand traegt (siehe
 * `TournamentDirectoryComponent.storeView`). Ausgelagert aus der Komponente, damit die nicht
 * ueber 1 000 Zeilen waechst — das Verhalten ist dasselbe.
 */
export interface DirectoryViewHost {
  tab: ViewTab;
  rangePreset: DirectoryRangePreset;
  filter: DirectoryFilter;
  placeLabel: string;
  calendarYear: number;
  calendarMonth: number;
  mapColourBy: PinColourBy;
  mapView: MapView | null;
  readonly speeds: TournamentSpeed[];
  applyRangePreset(preset: DirectoryRangePreset, reload?: boolean): void;
}

/** Was die Filterleiste zeigt — genug, um nach einem Seitenwechsel dasselbe Bild aufzubauen. */
export function snapshotView(host: DirectoryViewHost): Record<string, unknown> {
  return {
    tab: host.tab,
    rangePreset: host.rangePreset,
    from: host.filter.from,
    to: host.filter.to,
    federation: host.filter.federation,
    speed: host.filter.speed,
    text: host.filter.text,
    weekendOnly: host.filter.weekendOnly,
    minPlayers: host.filter.minPlayers,
    profileId: host.filter.profileId,
    lat: host.filter.lat,
    lon: host.filter.lon,
    radiusKm: host.filter.radiusKm,
    placeLabel: host.placeLabel,
    kinds: host.filter.kinds,
    ageGroups: host.filter.ageGroups,
    genders: host.filter.genders,
    adultsOnly: host.filter.adultsOnly,
    hideLeagues: host.filter.hideLeagues,
    includeIgnored: host.filter.includeIgnored,
    includeImplausible: host.filter.includeImplausible,
    calendarYear: host.calendarYear,
    calendarMonth: host.calendarMonth,
    mapColourBy: host.mapColourBy,
    mapView: host.mapView,
  };
}

/** Was `applyStoredView` ausser den Leistenfeldern zurueckmeldet — das bleibt in der Komponente. */
export interface AppliedStoredView {
  /** Ein selbst gesetzter Mittelpunkt kam aus dem Speicher (dann gilt `placeLabel` als gewaehlt). */
  placeRestored: boolean;
  storedProfileId: number | null;
  hasStoredProfile: boolean;
}

/** Den gemerkten Zustand auf die Leiste legen; `null`, wenn er unbrauchbar ist (dann bleibt alles). */
export function applyStoredView(
  host: DirectoryViewHost, stored: Record<string, unknown> | null): AppliedStoredView | null {
  if (!stored || typeof stored !== 'object') return null;

  const tab = stored['tab'];
  if (tab === 'list' || tab === 'map' || tab === 'calendar') host.tab = tab;

  const preset = stored['rangePreset'];
  if (typeof preset === 'string' && (DIRECTORY_RANGE_PRESETS as string[]).includes(preset)) {
    host.applyRangePreset(preset as DirectoryRangePreset, false);
    // Ein selbst gewaehlter Zeitraum steht nicht in rangeFor — der kommt aus dem Speicher.
    if (preset === 'custom') {
      host.filter.from = str(stored['from']);
      host.filter.to = str(stored['to']);
    }
  }

  host.filter.federation = str(stored['federation']);
  host.filter.text = str(stored['text']);
  host.filter.weekendOnly = stored['weekendOnly'] === true;
  host.filter.minPlayers = typeof stored['minPlayers'] === 'number' ? stored['minPlayers'] : null;
  host.filter.adultsOnly = stored['adultsOnly'] === true;
  host.filter.hideLeagues = stored['hideLeagues'] === true;
  host.filter.includeIgnored = stored['includeIgnored'] === true;
  host.filter.includeImplausible = stored['includeImplausible'] === true;

  const speed = stored['speed'];
  if (typeof speed === 'string' && host.speeds.includes(speed as TournamentSpeed)) {
    host.filter.speed = speed as TournamentSpeed;
  }

  const colourBy = stored['mapColourBy'];
  if (typeof colourBy === 'string' && (PIN_COLOUR_SCHEMES as string[]).includes(colourBy)) {
    host.mapColourBy = colourBy as PinColourBy;
  }
  // Ein alter Zustand ohne Ausschnitt startet wie bisher bei der Vorgabe.
  host.mapView = parseMapView(stored['mapView']);

  host.filter.kinds = pick(stored['kinds'], DIRECTORY_KINDS);
  host.filter.ageGroups = pick(stored['ageGroups'], DIRECTORY_AGE_GROUPS);
  host.filter.genders = pick(stored['genders'], DIRECTORY_GENDERS);

  // Ein selbst gesetzter Mittelpunkt gehoert zur Ansicht wie der Zeitraum — ihn nach jedem
  // Turnierbesuch neu zu suchen war der Grund, dass es diesen Speicher ueberhaupt gibt.
  const lat = stored['lat'];
  const lon = stored['lon'];
  const radiusKm = stored['radiusKm'];
  if (typeof radiusKm === 'number') host.filter.radiusKm = radiusKm;
  let placeRestored = false;
  if (typeof lat === 'number' && typeof lon === 'number') {
    host.filter.lat = lat;
    host.filter.lon = lon;
    host.placeLabel = str(stored['placeLabel']) ?? '';
    placeRestored = true;
  }

  // Das Profil wird erst uebernommen, wenn es die Liste noch kennt (applyQueryParams) —
  // ein geloeschtes Profil darf die Umkreissuche nicht auf tote Koordinaten stellen.
  const storedProfileId = typeof stored['profileId'] === 'number' ? stored['profileId'] : null;
  const hasStoredProfile = 'profileId' in stored;

  const year = stored['calendarYear'];
  const month = stored['calendarMonth'];
  if (typeof year === 'number' && typeof month === 'number' && month >= 1 && month <= 12) {
    host.calendarYear = year;
    host.calendarMonth = month;
  }
  return { placeRestored, storedProfileId, hasStoredProfile };
}

/**
 * Der gedrosselte Weg des Anzeigezustands zum Server samt Abgleich-Stand.
 *
 * <p>Der Server-Schreibvorgang ist gedrosselt: `storeView()` laeuft bei jeder Aenderung der
 * Leiste, und eine Tastatureingabe im Suchfeld sind ein Dutzend davon.</p>
 */
export class DirectoryViewSync {
  /**
   * Wie lange nach der letzten Aenderung gewartet wird, bevor der Zustand zum Server geht.
   * `storeView()` laeuft bei JEDER Aenderung der Leiste — eine Tastatureingabe im Suchfeld sind
   * ein Dutzend davon, und die will niemand als ein Dutzend Schreibvorgaenge sehen.
   */
  static readonly PersistDebounceMs = 1200;

  /** Der gedrosselte Weg zum Server. */
  private readonly persist = new Subject<Record<string, unknown>>();

  /**
   * Stand des Abgleichs mit dem Server (`syncStoredView`). Solange er aussteht, geht nichts hinauf:
   * der Server-Stand kann juenger sein (am anderen Geraet eingestellt), und schon der erste Aufbau
   * speichert die Ansicht. Scheitert der Abruf, geht nur hinauf, was der Nutzer DANACH geaendert hat —
   * der unveraenderte, womoeglich alte lokale Stand ueberschreibt den Server nicht (`mayPersist`).
   */
  private remoteSync: 'pending' | 'done' | 'failed' = 'pending';

  /**
   * Nach gescheitertem Abruf: der Zustand des FERTIGEN ersten Aufbaus, gegen den eine Aenderung des
   * Nutzers erkannt wird. `null`, solange der Aufbau noch laeuft — kommt der Fehler vor den
   * Suchprofilen, setzt `applyQueryParams` danach noch Profil, Ort und Umkreis, und das ist keine
   * Aenderung des Nutzers.
   */
  private stateAtSyncFailure: string | null = null;

  constructor(
    private readonly viewStates: ViewStateService,
    private readonly stateKey: string,
    private readonly destroyRef: DestroyRef,
  ) {}

  start(): void {
    this.persist.pipe(
      debounceTime(DirectoryViewSync.PersistDebounceMs),
      filter(state => this.mayPersist(state)),
      // Nichts schicken, was schon oben steht — das Umschalten zwischen zwei Reitern und zurueck
      // ist kein neuer Zustand.
      distinctUntilChanged((a, b) => JSON.stringify(a) === JSON.stringify(b)),
      switchMap(state => this.viewStates.save(this.stateKey, state)),
    ).subscribe();
    // Beim Verlassen der Seite den gedrosselten Stand NICHT verwerfen: `debounceTime` gibt beim
    // Abschluss seinen letzten Wert sofort heraus, und ein laufender Schreibvorgang darf zu Ende
    // laufen (danach ist die Kette von selbst fertig). Vorher kappte takeUntilDestroyed beides —
    // wer die Karte verschob und gleich ein Turnier oeffnete, fand beim Zurueckkommen den
    // aelteren Server-Stand vor, und der ueberschrieb den lokalen (Codereview 2026-09-29, F6-018).
    this.destroyRef.onDestroy(() => this.persist.complete());
  }

  queue(state: Record<string, unknown>): void {
    this.persist.next(state);
  }

  /** Abruf gescheitert; `baseline` ist der fertige erste Aufbau oder `null`, solange er noch laeuft. */
  markFailed(baseline: string | null): void {
    this.remoteSync = 'failed';
    this.stateAtSyncFailure = baseline;
  }

  markDone(): void {
    this.remoteSync = 'done';
  }

  /**
   * Scheiterte der Abruf schon vor dem fertigen Aufbau, ist erst JETZT der Stand da, an dem eine
   * Aenderung des Nutzers zu messen ist (siehe `stateAtSyncFailure`).
   */
  captureBaselineIfFailed(baseline: () => string): void {
    if (this.remoteSync === 'failed' && this.stateAtSyncFailure === null) {
      this.stateAtSyncFailure = baseline();
    }
  }

  /**
   * Darf dieser (gedrosselte) Zustand zum Server? Ja, wenn der Abgleich fertig ist. Nach gescheitertem
   * Abruf nur, wenn er vom fertigen ersten Aufbau abweicht — dann hat der Nutzer etwas geaendert, und
   * das ist juenger als jeder Server-Stand. Ab dieser ersten Aenderung gilt der Normalfall: sonst
   * ginge das Zuruecknehmen einer schon gesendeten Aenderung nie hinauf (es gliche ja wieder dem
   * Aufbau), und der Server bliebe auf dem verworfenen Zwischenstand stehen.
   */
  private mayPersist(state: Record<string, unknown>): boolean {
    if (this.remoteSync === 'done') return true;
    if (this.remoteSync === 'pending' || this.stateAtSyncFailure === null) return false;
    if (JSON.stringify(state) === this.stateAtSyncFailure) return false;
    this.remoteSync = 'done';
    this.stateAtSyncFailure = null;
    return true;
  }
}

/** Aus dem gemerkten Zustand: ein nicht leerer String oder `null`. Alles andere ist Muell. */
function str(value: unknown): string | null {
  return typeof value === 'string' && value.trim() ? value : null;
}

/** Aus dem gemerkten Zustand: ein brauchbarer Kartenausschnitt oder `null`. */
function parseMapView(value: unknown): MapView | null {
  if (!value || typeof value !== 'object') return null;
  const v = value as Record<string, unknown>;
  const finite = (n: unknown): n is number => typeof n === 'number' && Number.isFinite(n);
  if (!finite(v['lat']) || !finite(v['lon']) || !finite(v['zoom'])) return null;
  if (Math.abs(v['lat']) > 90 || Math.abs(v['lon']) > 180 || v['zoom'] < 0 || v['zoom'] > 18) return null;
  const centre = v['centre'];
  return { lat: v['lat'], lon: v['lon'], zoom: v['zoom'], centre: typeof centre === 'string' ? centre : null };
}

/**
 * Aus dem gemerkten Zustand: die Werte einer Liste, die es noch GIBT. Ein Wert, den eine spaetere
 * Fassung nicht mehr kennt, wuerde sonst als Filter weiterlaufen und die Liste unerklaerlich leer
 * halten — und der Server wiese ihn mit 400 ab.
 */
function pick<T extends string>(value: unknown, allowed: readonly T[]): T[] {
  return Array.isArray(value) ? value.filter((v): v is T => allowed.includes(v as T)) : [];
}

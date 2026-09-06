import {
  AfterViewInit, ChangeDetectionStrategy, Component, ElementRef, EventEmitter, Input,
  OnChanges, OnDestroy, Output, SimpleChanges, ViewChild, inject,
} from '@angular/core';
import { TranslateService } from '@ngx-translate/core';
import * as L from 'leaflet';
import { DirectoryEntry } from './tournament-directory.model';

/** Sichtbarer Kartenausschnitt als „minLat,minLon,maxLat,maxLon" — Serverformat. */
export type BoundsString = string;

/**
 * Leaflet-Karte mit den Turnier-Pins. Wie bei den Schachbrett-Komponenten besitzt die Komponente
 * die Bibliotheks-Instanz und spricht mit der Aussenwelt nur ueber Inputs und Outputs — Leaflet
 * taucht in keiner anderen Datei auf.
 *
 * Zwei bewusste Entscheidungen:
 *  - `preferCanvas` + `circleMarker` statt DOM-Marker: ein paar tausend Pins bringen die
 *    DOM-Variante zum Kriechen, im Canvas bleibt sie fluessig. Eine Cluster-Bibliothek waere eine
 *    weitere Abhaengigkeit fuer dasselbe Ergebnis.
 *  - Leaflets Stylesheet liegt in angular.json unter `styles` (global) und NICHT in dieser
 *    Komponente: die View-Encapsulation wuerde es wegkapseln und die Kachel-Positionierung
 *    zerlegen. Es sind ~15 kB — der Preis dafuer, dass die Karte ueberhaupt richtig sitzt.
 *  - Ein Klick auf einen Punkt oeffnet ein POPUP, nicht die Detailseite. Wer auf der Karte
 *    sucht, vergleicht — jeder Klick, der die Karte verlaesst, reisst diesen Faden ab (und den
 *    Ausschnitt gleich mit). Erst der Klick auf den TITEL im Popup fuehrt weiter.
 */
@Component({
  selector: 'app-tournament-map',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<div class="map-host" #mapEl [style.height]="height"></div>`,
  styles: [`
    .map-host {
      width: 100%;
      border-radius: 12px;
      overflow: hidden;
      background: var(--mat-sys-surface-container);
    }

    /* Leaflet haengt den Popup-Inhalt in einen EIGENEN Container ausserhalb dieses Templates —
       die View-Encapsulation erreicht ihn nicht. Deshalb ::ng-deep, auf den Host beschraenkt. */
    :host ::ng-deep .tm-popup { display: flex; flex-direction: column; gap: 4px; }

    :host ::ng-deep .tm-popup-title {
      display: block;
      width: 100%;
      padding: 0;
      border: 0;
      background: none;
      font: inherit;
      font-size: 0.95rem;
      font-weight: 600;
      line-height: 1.3;
      text-align: left;
      color: var(--mat-sys-primary);
      cursor: pointer;
      text-decoration: underline;
      text-underline-offset: 2px;
    }

    :host ::ng-deep .tm-popup-line { margin: 0; font-size: 0.82rem; }

    :host ::ng-deep .tm-popup-badges { display: flex; flex-wrap: wrap; gap: 4px; margin-top: 2px; }

    :host ::ng-deep .tm-badge {
      font-size: 0.72rem;
      padding: 1px 7px;
      border-radius: 999px;
      background: color-mix(in srgb, currentColor 12%, transparent);
    }

    :host ::ng-deep .tm-badge-warn {
      background: color-mix(in srgb, var(--mat-sys-error) 22%, transparent);
    }

    :host ::ng-deep .tm-popup-hint {
      margin: 2px 0 0;
      font-size: 0.72rem;
      opacity: 0.7;
    }
  `],
})
export class TournamentMapComponent implements AfterViewInit, OnChanges, OnDestroy {
  private readonly translate = inject(TranslateService);

  @ViewChild('mapEl', { static: true }) mapEl!: ElementRef<HTMLDivElement>;

  @Input() entries: DirectoryEntry[] = [];
  /** Mittelpunkt der Umkreissuche; zeichnet Kreis + Fadenkreuz. */
  @Input() centre: { lat: number; lon: number; radiusKm: number } | null = null;
  /**
   * Den Umkreis auch ZEICHNEN. Auf der Turnier-Detailseite dient der Mittelpunkt nur dem
   * Einpassen des Ausschnitts — ein eingefaerbter Kreis um den Austragungsort waere dort eine
   * Aussage ueber eine Umgebung, die niemand getroffen hat.
   */
  @Input() showRadius = true;
  /** Hoehe der Karte als CSS-Laenge; die Detailseite braucht eine kleinere als der Kalender. */
  @Input() height = 'min(70vh, 640px)';

  @Output() entrySelected = new EventEmitter<DirectoryEntry>();
  /** Feuert, wenn Kacheln nicht geladen werden koennen — sonst bleibt die Karte stumm schwarz. */
  @Output() tilesFailed = new EventEmitter<void>();
  /** Feuert nach jedem Verschieben/Zoomen mit dem neuen Ausschnitt. */
  @Output() boundsChanged = new EventEmitter<BoundsString>();

  private map?: L.Map;
  private markerLayer?: L.LayerGroup;
  private radiusLayer?: L.LayerGroup;
  private resizeObserver?: ResizeObserver;
  /** Auf welchen Mittelpunkt zuletzt eingepasst wurde — verhindert das Zurueckspringen beim Zoomen. */
  private lastFitted: string | null = null;

  ngAfterViewInit(): void {
    this.map = L.map(this.mapEl.nativeElement, {
      preferCanvas: true,
      center: [47.7, 13.4],   // Österreich als Startbild; der erste Filter zieht sofort nach
      zoom: 6,
      zoomControl: true,
    });

    // Gleiche Herkunft: nginx holt die Kachel bei OpenStreetMap und legt sie in seinen Cache.
    // Direkt aus dem Browser zu laden setzt voraus, dass JEDER Betrachter selbst ins offene Netz
    // kommt — ueber den WireGuard-Weg ist das nicht so, und die Karte blieb schwarz.
    const tiles = L.tileLayer('/tiles/{z}/{x}/{y}.png', {
      maxZoom: 18,
      // Pflichtangabe der OSM-Nutzungsbedingungen.
      attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a>',
    });
    // Eine stumm fehlschlagende Kachel sieht aus wie eine kaputte Seite. Einmal melden reicht.
    tiles.once('tileerror', () => this.tilesFailed.emit());
    tiles.addTo(this.map);

    this.markerLayer = L.layerGroup().addTo(this.map);
    this.radiusLayer = L.layerGroup().addTo(this.map);

    this.map.on('moveend', () => this.emitBounds());

    // Die Karte wird in einem mat-tab gerendert und startet deshalb oft mit Hoehe 0.
    // Ohne invalidateSize bleibt sie danach grau.
    this.resizeObserver = new ResizeObserver(() => {
      this.map?.invalidateSize();
      // Und der Ausschnitt muss NACHGEHOLT werden: passt Leaflet auf eine 0x0-Flaeche ein,
      // rechnet es die groesstmoegliche Vergroesserung aus — man landet tief in einer Strasse
      // statt beim ganzen Umkreis, und invalidateSize behaelt diese Vergroesserung bei.
      this.applyCentre();
    });
    this.resizeObserver.observe(this.mapEl.nativeElement);

    this.applyCentre();
    this.applyEntries();
    this.emitBounds();
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (!this.map) return;
    if (changes['entries']) this.applyEntries();
    if (changes['centre']) this.applyCentre();
  }

  ngOnDestroy(): void {
    this.resizeObserver?.disconnect();
    this.map?.remove();
    this.map = undefined;
  }

  private applyEntries(): void {
    if (!this.markerLayer) return;
    this.markerLayer.clearLayers();

    for (const entry of this.entries) {
      if (entry.lat == null || entry.lon == null) continue;

      const marker = L.circleMarker([entry.lat, entry.lon], {
        radius: 7,
        weight: 2,
        // Nur ungefaehr verortete Turniere (Bundesland-Mittelpunkt) sichtbar abschwaechen —
        // sonst suggeriert ein knackiger Pin eine Genauigkeit, die er nicht hat.
        color: entry.geoSource === 'Region' ? '#9aa0a6' : '#1a73e8',
        fillColor: entry.geoSource === 'Region' ? '#c8ccd0' : '#4285f4',
        fillOpacity: entry.geoSource === 'Region' ? 0.45 : 0.8,
      });

      marker.bindTooltip(tooltipHtml(entry), { direction: 'top', offset: [0, -6] });
      // Klick = Popup (siehe Klassenkommentar), NICHT der Sprung auf die Detailseite.
      marker.bindPopup(() => this.buildPopup(entry), { offset: [0, -4], minWidth: 220, maxWidth: 300 });
      // Beim geoeffneten Popup stuende der Hover-Hinweis mit demselben Inhalt daneben.
      marker.on('popupopen', () => marker.closeTooltip());
      marker.addTo(this.markerLayer);
    }
  }

  /**
   * Der Popup-Inhalt als echtes DOM statt als HTML-Zeichenkette: der Titel braucht einen
   * Klick-Horcher, und Turnier- und Ortsnamen kommen von chess-results — also fremder Text, der
   * in kein innerHTML gehoert. `textContent` macht die Frage gegenstandslos.
   */
  private buildPopup(entry: DirectoryEntry): HTMLElement {
    const root = document.createElement('div');
    root.className = 'tm-popup';

    const title = document.createElement('button');
    title.type = 'button';
    title.className = 'tm-popup-title';
    title.textContent = entry.name;
    title.title = this.text('tournamentDirectory.map.openDetail');
    title.addEventListener('click', () => this.entrySelected.emit(entry));
    root.appendChild(title);

    root.appendChild(this.line(dateRange(entry)));
    if (entry.location) root.appendChild(this.line(entry.location));

    const badges = document.createElement('div');
    badges.className = 'tm-popup-badges';
    const add = (label: string, warn = false) => {
      const span = document.createElement('span');
      span.className = warn ? 'tm-badge tm-badge-warn' : 'tm-badge';
      span.textContent = label;
      badges.appendChild(span);
    };

    if (entry.distanceKm !== null) add(this.text('tournamentDirectory.distance', { km: entry.distanceKm }));
    add(this.text('tournamentDirectory.speed.' + entry.speed));
    if (entry.playerCount) add(this.text('tournamentDirectory.players', { count: entry.playerCount }));
    if (entry.groupSize > 1) add(this.text('tournamentDirectory.groupCount', { count: entry.groupSize }));
    if (entry.cancelled) add(this.text('tournamentDirectory.cancelled'), true);
    root.appendChild(badges);

    const hint = document.createElement('p');
    hint.className = 'tm-popup-hint';
    hint.textContent = this.text('tournamentDirectory.map.openDetail');
    root.appendChild(hint);

    return root;
  }

  private line(value: string): HTMLElement {
    const p = document.createElement('p');
    p.className = 'tm-popup-line';
    p.textContent = value;
    return p;
  }

  /** `instant` genuegt hier: ein Popup gibt es erst nach einem Klick, die Texte stehen laengst. */
  private text(key: string, params?: Record<string, unknown>): string {
    return this.translate.instant(key, params);
  }

  private applyCentre(): void {
    if (!this.map || !this.radiusLayer) return;
    this.radiusLayer.clearLayers();
    if (!this.centre) { this.lastFitted = null; return; }

    const key = `${this.centre.lat}|${this.centre.lon}|${this.centre.radiusKm}`;

    const centre = L.latLng(this.centre.lat, this.centre.lon);
    if (this.showRadius) {
      // interactive: false ist hier keine Feinheit — eine Leaflet-Flaeche faengt Mausereignisse
      // standardmaessig ab. Der Umkreis liegt ueber den Turnieren, und innerhalb der eingefaerbten
      // Flaeche kam kein Mouseover mehr bei den Punkten an.
      L.circle(centre, {
        radius: this.centre.radiusKm * 1000,
        color: '#1a73e8',
        weight: 1,
        fillOpacity: 0.06,
        interactive: false,
      }).addTo(this.radiusLayer);
      L.circleMarker(centre, { radius: 4, color: '#1a73e8', fillOpacity: 1, interactive: false })
        .addTo(this.radiusLayer);
    }

    // NUR beim ersten Mal bzw. bei einem WIRKLICH anderen Mittelpunkt einpassen. Sonst zieht jede
    // Aenderungserkennung die Ansicht zurueck und Zoomen ist unmoeglich. Der Ausschnitt wird aus
    // dem Radius GERECHNET statt aus dem Kreis geholt: L.Circle.getBounds() braucht eine Karte
    // unter sich, und ohne gezeichneten Umkreis gibt es keinen Kreis, den man fragen koennte.
    //
    // Als eingepasst gilt es aber ERST, wenn die Flaeche eine Groesse hatte: auf 0x0 liefert
    // Leaflet die groesstmoegliche Vergroesserung, und die bliebe fuer immer stehen.
    const size = this.map.getSize();
    if (this.lastFitted !== key && size.x > 0 && size.y > 0) {
      this.lastFitted = key;
      this.map.fitBounds(centre.toBounds(this.centre.radiusKm * 2000), { padding: [16, 16] });
    }
  }

  private emitBounds(): void {
    if (!this.map) return;
    const b = this.map.getBounds();
    this.boundsChanged.emit(
      `${b.getSouth().toFixed(5)},${b.getWest().toFixed(5)},${b.getNorth().toFixed(5)},${b.getEast().toFixed(5)}`);
  }
}

function tooltipHtml(entry: DirectoryEntry): string {
  return `<strong>${escapeHtml(entry.name)}</strong><br>${escapeHtml(dateRange(entry))}` +
         (entry.location ? `<br>${escapeHtml(entry.location)}` : '');
}

/** „18.12. – 20.12." bzw. nur der eine Tag; leer, wenn chess-results gar kein Datum lieferte. */
function dateRange(entry: DirectoryEntry): string {
  const when = [entry.startDate, entry.endDate].filter(Boolean);
  return when.length === 2 && when[0] !== when[1] ? `${when[0]} – ${when[1]}` : (when[0] ?? '');
}

/** Turnier- und Ortsnamen kommen von chess-results — also fremder Text in einem innerHTML-Tooltip. */
function escapeHtml(value: string): string {
  return value
    .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;').replace(/'/g, '&#39;');
}

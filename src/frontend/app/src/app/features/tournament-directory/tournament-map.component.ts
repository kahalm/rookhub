import {
  AfterViewInit, ChangeDetectionStrategy, Component, ElementRef, EventEmitter, Input,
  OnChanges, OnDestroy, Output, SimpleChanges, ViewChild,
} from '@angular/core';
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
 */
@Component({
  selector: 'app-tournament-map',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<div class="map-host" #mapEl></div>`,
  styles: [`
    .map-host {
      width: 100%;
      height: min(70vh, 640px);
      border-radius: 12px;
      overflow: hidden;
      background: var(--mat-sys-surface-container);
    }
    @media (max-width: 768px) { .map-host { height: 60vh; } }
  `],
})
export class TournamentMapComponent implements AfterViewInit, OnChanges, OnDestroy {
  @ViewChild('mapEl', { static: true }) mapEl!: ElementRef<HTMLDivElement>;

  @Input() entries: DirectoryEntry[] = [];
  /** Mittelpunkt der Umkreissuche; zeichnet Kreis + Fadenkreuz. */
  @Input() centre: { lat: number; lon: number; radiusKm: number } | null = null;

  @Output() entrySelected = new EventEmitter<DirectoryEntry>();
  /** Feuert nach jedem Verschieben/Zoomen mit dem neuen Ausschnitt. */
  @Output() boundsChanged = new EventEmitter<BoundsString>();

  private map?: L.Map;
  private markerLayer?: L.LayerGroup;
  private radiusLayer?: L.LayerGroup;
  private resizeObserver?: ResizeObserver;

  ngAfterViewInit(): void {
    this.map = L.map(this.mapEl.nativeElement, {
      preferCanvas: true,
      center: [47.7, 13.4],   // Österreich als Startbild; der erste Filter zieht sofort nach
      zoom: 6,
      zoomControl: true,
    });

    L.tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png', {
      maxZoom: 18,
      // Pflichtangabe der OSM-Nutzungsbedingungen.
      attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a>',
    }).addTo(this.map);

    this.markerLayer = L.layerGroup().addTo(this.map);
    this.radiusLayer = L.layerGroup().addTo(this.map);

    this.map.on('moveend', () => this.emitBounds());

    // Die Karte wird in einem mat-tab gerendert und startet deshalb oft mit Hoehe 0.
    // Ohne invalidateSize bleibt sie danach grau.
    this.resizeObserver = new ResizeObserver(() => this.map?.invalidateSize());
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
      marker.on('click', () => this.entrySelected.emit(entry));
      marker.addTo(this.markerLayer);
    }
  }

  private applyCentre(): void {
    if (!this.map || !this.radiusLayer) return;
    this.radiusLayer.clearLayers();
    if (!this.centre) return;

    const centre: L.LatLngExpression = [this.centre.lat, this.centre.lon];
    const circle = L.circle(centre, {
      radius: this.centre.radiusKm * 1000,
      color: '#1a73e8',
      weight: 1,
      fillOpacity: 0.06,
    });
    circle.addTo(this.radiusLayer);
    L.circleMarker(centre, { radius: 4, color: '#1a73e8', fillOpacity: 1 }).addTo(this.radiusLayer);

    this.map.fitBounds(circle.getBounds(), { padding: [16, 16] });
  }

  private emitBounds(): void {
    if (!this.map) return;
    const b = this.map.getBounds();
    this.boundsChanged.emit(
      `${b.getSouth().toFixed(5)},${b.getWest().toFixed(5)},${b.getNorth().toFixed(5)},${b.getEast().toFixed(5)}`);
  }
}

function tooltipHtml(entry: DirectoryEntry): string {
  const when = [entry.startDate, entry.endDate].filter(Boolean);
  const range = when.length === 2 && when[0] !== when[1] ? `${when[0]} – ${when[1]}` : (when[0] ?? '');
  return `<strong>${escapeHtml(entry.name)}</strong><br>${escapeHtml(range)}` +
         (entry.location ? `<br>${escapeHtml(entry.location)}` : '');
}

/** Turnier- und Ortsnamen kommen von chess-results — also fremder Text in einem innerHTML-Tooltip. */
function escapeHtml(value: string): string {
  return value
    .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;').replace(/'/g, '&#39;');
}

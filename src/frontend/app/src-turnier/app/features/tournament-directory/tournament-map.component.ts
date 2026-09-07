import {
  AfterViewInit, ChangeDetectionStrategy, Component, ComponentRef, ElementRef, EventEmitter,
  Input, OnChanges, OnDestroy, Output, SimpleChanges, ViewChild, ViewContainerRef, inject,
} from '@angular/core';
import { TranslateService } from '@ngx-translate/core';
import * as L from 'leaflet';
import { MapPinMarker } from './map-pin-marker';
import { TournamentCardComponent } from './tournament-card.component';
import { DirectoryEntry, DirectoryVenue } from './tournament-directory.model';

/** Sichtbarer Kartenausschnitt als „minLat,minLon,maxLat,maxLon" — Serverformat. */
export type BoundsString = string;

/** Kopfradius eines Turnier-Pins in Pixeln. */
const PinRadius = 7;

/** Wie hoch der Pin ueber seinem Ort steht — Ausrichtung von Popup und Hinweis. */
const PinHeight = MapPinMarker.heightAbove(PinRadius);

/**
 * Leaflet-Karte mit den Turnier-Pins. Wie bei den Schachbrett-Komponenten besitzt die Komponente
 * die Bibliotheks-Instanz und spricht mit der Aussenwelt nur ueber Inputs und Outputs — Leaflet
 * taucht in keiner anderen Datei auf.
 *
 * Zwei bewusste Entscheidungen:
 *  - `preferCanvas` statt DOM-Marker: ein paar tausend Marken bringen die DOM-Variante zum
 *    Kriechen, im Canvas bleibt sie fluessig. Eine Cluster-Bibliothek waere eine weitere
 *    Abhaengigkeit fuer dasselbe Ergebnis. Die Pin-FORM (unten spitz, oben rund) kommt deshalb
 *    aus einer eigenen Canvas-Marke, `MapPinMarker` — sie zeigt auf ihren Ort, waehrend ein
 *    Kreis behauptet, der Ort liege in seiner schwer zu treffenden Mitte.
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
  /**
   * Fuer das Popup: Leaflet haelt seinen Inhalt in einem EIGENEN Container ausserhalb dieses
   * Templates, die Kurzansicht muss dort also von Hand erzeugt und wieder abgeraeumt werden.
   */
  private readonly viewContainer = inject(ViewContainerRef);

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
  /**
   * Nach dem Einpassen um so viele Stufen HERAUSzoomen. Auf der Detailseite eine: der eingepasste
   * Ausschnitt sitzt so knapp um den Ort, dass die Umgebung fehlt, an der man ihn erkennt.
   * Ausdruecklich als Stufen und nicht ueber einen groesseren Radius — „eine Stufe" ist dann auch
   * genau eine, unabhaengig von Seitenverhaeltnis und Randabstand.
   */
  @Input() zoomOutSteps = 0;

  @Output() entrySelected = new EventEmitter<DirectoryEntry>();
  /** Feuert, wenn Kacheln nicht geladen werden koennen — sonst bleibt die Karte stumm schwarz. */
  @Output() tilesFailed = new EventEmitter<void>();
  /** Feuert nach jedem Verschieben/Zoomen mit dem neuen Ausschnitt. */
  @Output() boundsChanged = new EventEmitter<BoundsString>();
  /** Ein Turnier wurde aus dem Popup heraus aus- oder wieder eingeblendet. */
  @Output() entryIgnored = new EventEmitter<{ entry: DirectoryEntry; ignored: boolean }>();

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
    // Erst NACH dem laufenden Durchlauf melden: der Elternteil setzt daraufhin sein Ladeflag,
    // das in seinem Template schon gelesen wurde — im Dev-Build ist das ein NG0100.
    queueMicrotask(() => this.emitBounds());
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (!this.map) return;
    if (changes['entries']) this.applyEntries();
    if (changes['centre']) this.applyCentre();
  }

  ngOnDestroy(): void {
    this.destroyPopup();
    this.resizeObserver?.disconnect();
    this.map?.remove();
    this.map = undefined;
  }

  private applyEntries(): void {
    if (!this.markerLayer) return;
    // Die Marker verschwinden — mit ihnen das offene Popup, dessen Komponente sonst haengen
    // bleibt.
    this.destroyPopup();
    this.markerLayer.clearLayers();
    this.markersByEntry.clear();

    // Gemerkte ZULETZT: Leaflet zeichnet in Reihenfolge des Hinzufuegens, und ein gemerkter Punkt
    // soll nicht unter einem beliebigen anderen liegen. In einer Stadt mit dreissig Turnieren ist
    // genau das der Unterschied zwischen „ich sehe meins" und „ich suche meins".
    const ordered = [...this.entries].sort(
      (a, b) => Number(a.subscribed) - Number(b.subscribed));

    for (const entry of ordered) {
      // Ein Punkt JE SPIELORT: bei Ligen nennt chess-results mehrere („Mayrhofen, St.Veit").
      // Mit nur dem Hauptort verschwaende ein Turnier die Haelfte seiner Orte, und die
      // Umkreissuche fand es nicht, obwohl es zur Haelfte vor der Haustuer stattfindet.
      for (const spot of venuesOf(entry)) {
      const marker = new MapPinMarker([spot.lat, spot.lon], {
        radius: PinRadius,
        ...pinStyle(entry, spot),
      });

      // Popup und Hinweis muessen ueber den KOPF des Pins ausgerichtet werden, nicht ueber
      // seinen Ankerpunkt — sonst liegen sie mitten auf der Marke.
      marker.bindTooltip(tooltipHtml(entry, spot), { direction: 'top', offset: [0, -PinHeight] });
      // Klick = Popup (siehe Klassenkommentar), NICHT der Sprung auf die Detailseite.
      marker.bindPopup(() => this.buildPopup(entry, spot),
        { offset: [0, -PinHeight + 4], minWidth: 220, maxWidth: 300 });
      // Beim geoeffneten Popup stuende der Hover-Hinweis mit demselben Inhalt daneben.
      marker.on('popupopen', () => marker.closeTooltip());
      marker.addTo(this.markerLayer);
      // Fuer das Umfaerben ohne Neuladen (siehe applySubscribed).
      const known = this.markersByEntry.get(entry.id);
      if (known) known.push({ marker, spot }); else this.markersByEntry.set(entry.id, [{ marker, spot }]);
      }
    }
  }

  /**
   * Alle Punkte EINES Turniers samt ihrem Spielort — ein Turnier kann mehrere haben, und jeder
   * traegt seine eigene Genauigkeit (die in den Stil eingeht).
   */
  private readonly markersByEntry =
    new Map<string, { marker: MapPinMarker; spot: DirectoryVenue }[]>();

  /**
   * Faerbt die Punkte eines Turniers um, nachdem sich „gemerkt" geaendert hat.
   *
   * <p>Bewusst kein Neuladen des Ausschnitts: das wuerde die Marker wegwerfen und damit das
   * Popup zuschlagen, in dem der Nutzer gerade geklickt hat. Der Eintrag wird ebenfalls
   * mitgeschrieben, damit ein spaeteres `applyEntries` (Ausschnitt verschoben) denselben Zustand
   * zeichnet.</p>
   */
  applySubscribed(entry: DirectoryEntry, subscribed: boolean): void {
    entry.subscribed = subscribed;
    for (const { marker, spot } of this.markersByEntry.get(entry.id) ?? [])
      marker.setStyle(pinStyle(entry, spot));
  }

  /**
   * Der Popup-Inhalt als echtes DOM statt als HTML-Zeichenkette: der Titel braucht einen
   * Klick-Horcher, und Turnier- und Ortsnamen kommen von chess-results — also fremder Text, der
   * in kein innerHTML gehoert. `textContent` macht die Frage gegenstandslos.
   */
  /**
   * Der Popup-Inhalt ist die GEMEINSAME Kurzansicht (`TournamentCardComponent`) — dieselbe wie in
   * Liste und Kalender, mit denselben vier Aktionen. Vorher war das hier ein von Hand gebauter
   * DOM-Baum ohne Aktionen: wer auf der Karte ein Turnier fand, musste erst auf die Detailseite,
   * um es zu merken.
   *
   * <para>Erzeugt wird sie dynamisch, weil Leaflet den Popup-Inhalt in einem eigenen Container
   * ausserhalb dieses Templates haelt. Ueber den ViewContainerRef bleibt sie trotzdem Teil der
   * Aenderungserkennung dieser Komponente — nur so aktualisieren sich die Symbole nach einem
   * Klick.</para>
   *
   * <para>Es gibt immer nur EIN offenes Popup; die vorige Ansicht wird deshalb beim Erzeugen der
   * naechsten abgeraeumt und in `ngOnDestroy` ein letztes Mal. Ohne das haengt je geoeffnetem
   * Punkt eine Komponente samt Abonnements im Speicher.</para>
   */
  private buildPopup(entry: DirectoryEntry, spot: DirectoryVenue): HTMLElement {
    this.destroyPopup();

    const card = this.viewContainer.createComponent(TournamentCardComponent);
    card.setInput('entry', entry);
    card.setInput('overview', true);
    card.setInput('venueName', spot.name);
    card.instance.selected.subscribe(selected => this.entrySelected.emit(selected));
    card.instance.ignoredChanged.subscribe(change => this.entryIgnored.emit(change));
    // Nur umfaerben, nicht neu laden — sonst schlaegt das gerade offene Popup zu.
    card.instance.subscribedChanged.subscribe(
      change => this.applySubscribed(change.entry, change.subscribed));
    // Sofort rendern: Leaflet erwartet ein FERTIGES Element und misst danach die Popup-Groesse.
    card.changeDetectorRef.detectChanges();

    this.popup = card;
    return card.location.nativeElement as HTMLElement;
  }

  private popup?: ComponentRef<TournamentCardComponent>;

  private destroyPopup(): void {
    this.popup?.destroy();
    this.popup = undefined;
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
      // animate: false — sonst ist der Zoom beim ersten Bericht des Ausschnitts noch nicht
      // angewandt (die Animation laeuft asynchron), und die Karte startet mit einer Bewegung,
      // die niemand ausgeloest hat.
      if (this.zoomOutSteps > 0) {
        this.map.setZoom(this.map.getZoom() - this.zoomOutSteps, { animate: false });
      }
    }
  }

  private emitBounds(): void {
    if (!this.map) return;
    const b = this.map.getBounds();
    this.boundsChanged.emit(
      `${b.getSouth().toFixed(5)},${b.getWest().toFixed(5)},${b.getNorth().toFixed(5)},${b.getEast().toFixed(5)}`);
  }
}

/**
 * Wie ein Punkt aussieht. Zwei Angaben stecken darin, und beide muessen ohne Legende ablesbar
 * sein:
 *
 * <p><b>Gemerkt</b> — ein eigener FARBTON (Amber statt Blau) plus ein dickerer Ring. Zwei Kanaele
 * bewusst: Farbe allein trennt fuer einen Teil der Betrachter nicht, und auf einer Karte mit
 * dreissig blauen Punkten in einer Stadt ist „meins" sonst nicht zu finden. Vorher unterschied
 * der Punkt es GAR NICHT — die Auskunft stand nur im Popup, also erst nach einem Klick auf den
 * richtigen Punkt, den man ohne die Auskunft nicht kennt.</p>
 *
 * <p><b>Nur ungefaehr verortet</b> (Bundesland-Mittelpunkt) — sichtbar abgeschwaecht, sonst
 * suggeriert ein knackiger Pin eine Genauigkeit, die er nicht hat. Diese Abschwaechung gilt auch
 * fuer gemerkte, damit die Aussage nicht verlorengeht.</p>
 */
function pinStyle(entry: DirectoryEntry, spot: DirectoryVenue): L.PathOptions {
  const vague = spot.geoSource === 'Region';
  if (entry.subscribed) {
    return {
      // Kraeftiger Rand + dicker Ring: der Punkt soll aus einer blauen Menge herausstechen.
      color: '#b06000',
      fillColor: '#f9ab00',
      weight: 3,
      fillOpacity: vague ? 0.55 : 0.95,
    };
  }
  return {
    color: vague ? '#9aa0a6' : '#1a73e8',
    fillColor: vague ? '#c8ccd0' : '#4285f4',
    weight: 2,
    fillOpacity: vague ? 0.45 : 0.8,
  };
}

function tooltipHtml(entry: DirectoryEntry, spot: DirectoryVenue): string {
  const place = entry.venues.length > 1 ? spot.name : (entry.location ?? '');
  return `<strong>${escapeHtml(entry.name)}</strong><br>${escapeHtml(dateRange(entry))}` +
         (place ? `<br>${escapeHtml(place)}` : '');
}

/**
 * Die zu zeichnenden Punkte eines Turniers: die Liste der Spielorte, wenn es mehrere gibt, sonst
 * der eine Ort am Eintrag selbst. Nicht verortete Turniere liefern nichts.
 */
function venuesOf(entry: DirectoryEntry): DirectoryVenue[] {
  if (entry.venues.length > 0) return entry.venues;
  if (entry.lat == null || entry.lon == null) return [];
  return [{ name: entry.geoPlaceName ?? entry.location ?? '', lat: entry.lat, lon: entry.lon,
            geoSource: entry.geoSource }];
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

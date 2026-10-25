import {
  AfterViewInit, ChangeDetectionStrategy, Component, ComponentRef, ElementRef, EventEmitter,
  Input, OnChanges, OnDestroy, Output, SimpleChanges, ViewChild, ViewContainerRef, inject,
  signal,
} from '@angular/core';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import * as L from 'leaflet';
import { MapPinMarker, pinRadiusFor } from './map-pin-marker';
import { TournamentCardComponent } from './tournament-card.component';
import { DirectoryEntry } from './tournament-directory.model';
import { PinGroup, PinMember, groupByPoint, groupStyle } from './map-pin-groups';
import { MapPopupBuilder, MapText } from './map-popup-builder';
import { MIXED, PinColourBy, commonCategory } from './pin-palette';
import { MapLegendComponent } from './map-legend.component';

/** Sichtbarer Kartenausschnitt als „minLat,minLon,maxLat,maxLon" — Serverformat. */
export type BoundsString = string;

/**
 * Ein Kartenausschnitt zum Wiederherstellen: Mitte, Zoomstufe und der Umkreis, unter dem er
 * galt (`lat|lon|radiusKm` wie in `applyCentre`, `null` = ohne Umkreis). Mitte + Zoom statt der
 * Grenzen: ein `fitBounds` auf die alten Grenzen landet je nach Rundung eine Stufe daneben.
 */
export interface MapView {
  lat: number;
  lon: number;
  zoom: number;
  centre: string | null;
}

/**
 * Kopfradius eines Turnier-Pins in Pixeln — der Wert fuer EIN Turnier. Ein gebuendelter Punkt
 * waechst darueber hinaus, damit seine Anzahl hineinpasst (`pinRadiusFor`); wie hoch er dann
 * ueber seinem Ort steht, sagt `MapPinMarker.heightAbove`.
 */
const PinRadius = 7;

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
 *    Kreis behauptet, der Ort liege in seiner schwer zu treffenden Mitte. Auf Touch-Geraeten
 *    bekommt der Canvas-Renderer 8 px Klick-Toleranz: ein einzelner Pin ist sonst nur ~16 px
 *    breit und mit dem Finger kaum zu treffen. NUR dort — die Maus ist praezise, und der
 *    Hover-Hinweis soll nicht 8 px neben dem Pin aufgehen.
 *  - Leaflets Stylesheet liegt in angular.json unter `styles` (global) und NICHT in dieser
 *    Komponente: die View-Encapsulation wuerde es wegkapseln und die Kachel-Positionierung
 *    zerlegen. Es sind ~15 kB — der Preis dafuer, dass die Karte ueberhaupt richtig sitzt.
 *  - Ein Klick auf einen Punkt oeffnet ein POPUP, nicht die Detailseite. Wer auf der Karte
 *    sucht, vergleicht — jeder Klick, der die Karte verlaesst, reisst diesen Faden ab (und den
 *    Ausschnitt gleich mit). Erst der Klick auf den TITEL im Popup fuehrt weiter.
 *  - EIN Punkt je KOORDINATE, nicht je Turnier: liegen zwei Pins exakt uebereinander, ist der
 *    untere unerreichbar — er hat keine freie Flaeche, ueber der ein Klick bei ihm ankaeme.
 *    Gemeldet an zwei Tiroler Ligen, die beide nur „Tirol" als Ortstext tragen und deshalb
 *    beide auf der Landesmitte sitzen (dort liegen fuenf, anderswo bis zu 111). Bewusst KEIN
 *    Auseinanderruecken um ein paar Pixel: das behauptete Positionen, die niemand kennt. Der
 *    gebuendelte Pin traegt stattdessen die ANZAHL, und sein Popup listet alle auf.
 *  - Gesten, die die SEITE meinen, bleiben bei der Seite. Die Karte ist bis zu 70 % der
 *    Bildschirmhoehe gross; mit Leaflets Vorgaben zoomte das Mausrad sie, sobald der Zeiger beim
 *    Hinunterscrollen darueber kam, und am Handy verschob jeder Wisch, der auf ihr begann, Europa
 *    statt der Seite. Deshalb zoomt das Rad erst nach einem Klick in die Karte (bis der Zeiger sie
 *    verlaesst), und auf Touch-Geraeten scrollt EIN Finger die Seite, ZWEI verschieben und zoomen
 *    die Karte (Leaflets Kneif-Zoom verschiebt dabei mit). Wer es anders versucht, bekommt einen
 *    kurzen Hinweis, statt dass scheinbar nichts geschieht.
 */
@Component({
  selector: 'app-tournament-map',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslatePipe, MapLegendComponent],
  template: `
    <div class="map-wrap" #wrapEl>
      <div class="map-host" #mapEl [style.height]="height"></div>

      <!-- Nur ein Fingerzeig, keine Meldung: er erscheint bei jedem Scrollen ueber der Karte und
           waere fuer einen Screenreader Laerm (die Tastatur bedient die Karte ohnehin). -->
      @if (gestureHint(); as hint) {
        <div class="gesture-hint" aria-hidden="true">
          {{ 'tournamentDirectory.map.gestureHint.' + hint | translate }}
        </div>
      }

      <!-- Kartenzubehoer, kein Formular: es steht UEBER der Karte und darf ihre Ereignisse nicht
           ausloesen (siehe disableClickPropagation in ngAfterViewInit). -->
      <div class="map-chrome" #chromeEl [hidden]="!showChrome">
        <app-map-legend [colourBy]="colourBy" [mixed]="mixedShown()" (colourPicked)="pickColourBy($event)" />
      </div>
    </div>
  `,
  styleUrls: ['./tournament-map.component.scss'],
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
  /**
   * Wonach die Punkte eingefaerbt werden. Kommt von aussen, damit die Wahl den Weg
   * „Turnier oeffnen -> zurueck" und den Geraetewechsel ueberlebt (der Elternteil legt sie in
   * seinen gespeicherten Anzeigezustand).
   */
  @Input() colourBy: PinColourBy = 'speed';
  /**
   * Das Zubehoer (Einfaerben-Auswahl + Legende) ueberhaupt ZEIGEN. Die Detailseite zeigt genau
   * einen Pin — dort erklaert die Legende nichts und die Auswahl faerbt nichts um, das Bedienfeld
   * verdeckte auf dem Handy aber ein Viertel der kleinen Karte. Ausgeblendet per [hidden], nicht
   * per @if: das Element bleibt im DOM, der statische ViewChild und disableClickPropagation
   * darauf laufen unveraendert.
   */
  @Input() showChrome = true;
  /**
   * Hier STARTEN statt bei Oesterreich, Zoom 6. Die Karte steht beim Elternteil in einem
   * `@if` und wird bei jedem Reiterwechsel und nach „Turnier oeffnen -> zurueck" neu gebaut —
   * der verschobene Ausschnitt war dann jedes Mal weg (Codereview 2026-09-29, F6-018). Gilt der
   * Ausschnitt fuer denselben Umkreis wie jetzt, wird nicht neu eingepasst; bei einem anderen
   * Umkreis gewinnt das Einpassen. Nur beim Aufbau gelesen — der Umkreis (`centre`) muss dann
   * also schon feststehen. Kaeme er erst danach, passte `applyCentre` ein; der Kalender baut die
   * Karte deshalb erst, wenn die Suchprofile angewandt sind.
   */
  @Input() initialView: MapView | null = null;

  @Output() entrySelected = new EventEmitter<DirectoryEntry>();
  /** Feuert, wenn Kacheln nicht geladen werden koennen — sonst bleibt die Karte stumm schwarz. */
  @Output() tilesFailed = new EventEmitter<void>();
  /** Feuert nach jedem Verschieben/Zoomen mit dem neuen Ausschnitt. */
  @Output() boundsChanged = new EventEmitter<BoundsString>();
  /** Ein Turnier wurde aus dem Popup heraus aus- oder wieder eingeblendet. */
  @Output() entryIgnored = new EventEmitter<{ entry: DirectoryEntry; ignored: boolean }>();
  /** Der Betrachter hat ein anderes Merkmal zum Einfaerben gewaehlt. */
  @Output() colourByChange = new EventEmitter<PinColourBy>();
  /**
   * Feuert mit `boundsChanged` — einen Mikrotask spaeter (siehe `emitBounds`): der Ausschnitt
   * zum Wiederherstellen (siehe `initialView`).
   */
  @Output() viewChanged = new EventEmitter<MapView>();

  @ViewChild('chromeEl', { static: true }) chromeEl!: ElementRef<HTMLDivElement>;

  /**
   * Steht auf der Karte gerade ein gebuendelter Punkt mit UNTERSCHIEDLICHEN Klassen? Nur dann
   * gehoert „gemischt" in die Legende — sonst erklaerte sie eine Farbe, die nirgends vorkommt.
   *
   * <p>Ein SIGNAL, kein Feld: gesetzt wird es in `applyEntries`, und das laeuft auch aus
   * `ngAfterViewInit` — also nach dem ersten Zeichnen. Bei OnPush bliebe die Zeile „gemischt"
   * dann bis zur naechsten Aenderung unsichtbar.</p>
   */
  protected readonly mixedShown = signal(false);

  /**
   * Welcher Gesten-Hinweis gerade ueber der Karte steht: `wheel` (Mausrad, bevor jemand in die
   * Karte geklickt hat) oder `touch` (ein Finger, der die Seite scrollt statt die Karte).
   */
  readonly gestureHint = signal<'wheel' | 'touch' | null>(null);
  private gestureHintTimer?: ReturnType<typeof setTimeout>;

  pickColourBy(chosen: PinColourBy): void {
    if (chosen === this.colourBy) return;
    this.colourBy = chosen;
    this.applyEntries();
    this.colourByChange.emit(chosen);
  }

  private map?: L.Map;
  private markerLayer?: L.LayerGroup;
  private radiusLayer?: L.LayerGroup;
  private resizeObserver?: ResizeObserver;
  /** Auf welchen Mittelpunkt zuletzt eingepasst wurde — verhindert das Zurueckspringen beim Zoomen. */
  private lastFitted: string | null = null;

  ngAfterViewInit(): void {
    // Touch-Geraet? Dann zaehlt ein Tipp ein paar Pixel neben dem Pin noch als Treffer (siehe
    // Klassenkommentar). Defensiv: nicht jede Testumgebung kennt matchMedia.
    const coarse = window.matchMedia?.('(pointer: coarse)').matches ?? false;
    const start = this.initialView;
    this.map = L.map(this.mapEl.nativeElement, {
      preferCanvas: true,
      renderer: L.canvas({ tolerance: coarse ? 8 : 0 }),
      // Österreich als Startbild; der erste Filter zieht sofort nach — ausser es gibt einen
      // gemerkten Ausschnitt (siehe initialView).
      center: start ? [start.lat, start.lon] : [47.7, 13.4],
      zoom: start?.zoom ?? 6,
      zoomControl: true,
      // Seiten-Gesten bleiben bei der Seite (Klassenkommentar): das Rad erst nach einem Klick,
      // auf Touch-Geraeten verschieben zwei Finger statt einem.
      scrollWheelZoom: false,
      dragging: !coarse,
    });
    // Der gemerkte Ausschnitt gehoert zu DIESEM Umkreis: dann gilt er als eingepasst, sonst
    // zoege `applyCentre` die Ansicht sofort wieder auf den ganzen Kreis zurueck.
    if (start && start.centre !== null && start.centre === this.centreKey()) this.lastFitted = start.centre;
    this.wireGestures(coarse);

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
    // Leaflet schiebt die Karte selbst, damit ein Popup am Rand ins Bild passt. Dieses Schieben
    // ist KEIN neuer Ausschnitt im Sinne des Elternteils: meldete man es, laedt er die Punkte
    // neu, `applyEntries` wirft alle Marker weg — und mit ihnen genau das Popup, fuer das die
    // Karte eben gerueckt ist. Gemeldet als „nach dem Verschieben verschwindet der Hinweis".
    this.map.on('autopanstart', () => (this.autoPanning = true));
    // Beim Umschalten Liste <-> Einzelansicht aendert sich die Groesse des Inhalts; ohne ein
    // `update()` behaelt Leaflet die alte Kachelgroesse und der Inhalt haengt heraus.
    this.map.on('popupopen', e => (this.openPopup = (e as L.PopupEvent).popup));
    this.map.on('popupclose', () => { this.openPopup = undefined; this.forgetPopup(); });

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

    // Ohne das zieht ein Klick auf die Auswahl die Karte mit und das Rad darueber zoomt sie.
    L.DomEvent.disableClickPropagation(this.chromeEl.nativeElement);
    L.DomEvent.disableScrollPropagation(this.chromeEl.nativeElement);

    this.applyCentre();
    this.applyEntries();
    // Erst NACH dem laufenden Durchlauf melden: der Elternteil setzt daraufhin sein Ladeflag,
    // das in seinem Template schon gelesen wurde — im Dev-Build ist das ein NG0100.
    queueMicrotask(() => this.emitBounds());
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (!this.map) return;
    if (changes['entries'] || changes['colourBy']) this.applyEntries();
    if (changes['centre']) this.applyCentre();
  }

  ngOnDestroy(): void {
    clearTimeout(this.gestureHintTimer);
    this.forgetPopup();
    this.resizeObserver?.disconnect();
    this.map?.remove();
    this.map = undefined;
  }

  /**
   * Schaltet das Mausrad-Zoomen mit dem Klick in die Karte ein und beim Verlassen wieder aus;
   * zeigt den Hinweis, wenn eine Geste die Karte nicht bewegt. `mouseleave` und nicht Leaflets
   * `mouseout`: Letzteres feuert auch beim Verlassen eines Punktes und schaltete das Rad mitten
   * auf der Karte wieder ab. Die beiden Hinweis-Lauscher sind `passive` — sie duerfen das
   * Scrollen der Seite, das sie begleiten, nicht aufhalten.
   */
  private wireGestures(coarse: boolean): void {
    const map = this.map!;
    const host = this.mapEl.nativeElement;
    // In einem Popup (Turnierliste eines Punktes) oder auf Leaflets Knoepfen ist ein Finger
    // richtig — dort scrollt bzw. tippt man, die Karte soll sich gar nicht bewegen.
    const onMapItself = (e: Event) =>
      !(e.target as Element | null)?.closest?.('.leaflet-popup, .leaflet-control');
    if (coarse) {
      host.addEventListener('touchmove', e => {
        if (e.touches.length === 1 && onMapItself(e)) this.showGestureHint('touch');
      }, { passive: true });
      return;
    }
    const enableWheel = () => map.scrollWheelZoom.enable();
    host.addEventListener('mousedown', enableWheel);
    map.on('focus', enableWheel);   // auch per Tab in die Karte
    host.addEventListener('mouseleave', () => map.scrollWheelZoom.disable());
    host.addEventListener('wheel', e => {
      if (!map.scrollWheelZoom.enabled() && onMapItself(e)) this.showGestureHint('wheel');
    }, { passive: true });
  }

  private showGestureHint(kind: 'wheel' | 'touch'): void {
    this.gestureHint.set(kind);
    clearTimeout(this.gestureHintTimer);
    this.gestureHintTimer = setTimeout(() => this.gestureHint.set(null), 1500);
  }

  private applyEntries(): void {
    if (!this.markerLayer) return;
    // Die Marker verschwinden — mit ihnen das offene Popup, dessen Komponente sonst haengen
    // bleibt.
    this.forgetPopup();
    this.markerLayer.clearLayers();
    this.groupsByEntry.clear();

    let mixed = false;
    // Das Popup darf nicht breiter sein als die Karte: auf 360 px war es 3 px zu breit, vom
    // Schliessen-X fehlte ein Drittel. 60 = Leaflets Popup-Rand (44) plus autoPan (2x5) plus
    // Luft. Ohne Groesse (Karte im noch unsichtbaren mat-tab) gelten die Vorgaben; nach einer
    // Rotation heilt es sich, weil moveend neue Eintraege bringt und hier neu gebunden wird.
    const mapWidth = this.map?.getSize().x;
    const avail = mapWidth ? Math.max(120, mapWidth - 60) : 300;

    for (const group of groupByPoint(this.entries)) {
      const count = group.members.length;
      // Die Klasse gehoert dem BUENDEL: liegen Turniere verschiedener Klassen auf demselben
      // Punkt, darf er keine behaupten (siehe MIXED).
      group.category = commonCategory(group.members.map(m => m.entry), this.colourBy);
      if (group.category === MIXED && count > 1) mixed = true;
      // Der Kopf waechst mit der Stellenzahl (siehe pinRadiusFor) — damit waechst auch, wie hoch
      // der Pin ueber seinem Ort steht, und Popup und Hinweis muessen das mitmachen.
      const radius = pinRadiusFor(PinRadius, count);
      const above = MapPinMarker.heightAbove(radius);

      const marker = new MapPinMarker([group.lat, group.lon], {
        radius,
        count,
        labelColor: group.category.darkLabel ? '#202124' : '#fff',
        ...groupStyle(group),
      });
      group.marker = marker;

      // Als FUNKTION, nicht als fertiger Text: der Hinweis wird uebersetzt, und beim Zeichnen der
      // Marker koennen die Sprachdateien noch unterwegs sein.
      marker.bindTooltip(
        () => MapPopupBuilder.tooltipHtml(group, this.text, this.translate.currentLang()),
        { direction: 'top', offset: [0, -above] });
      // Klick = Popup (siehe Klassenkommentar), NICHT der Sprung auf die Detailseite.
      marker.bindPopup(() => this.buildPopup(group), {
        offset: [0, -above + 4], minWidth: Math.min(220, avail), maxWidth: Math.min(300, avail),
      });
      // Beim geoeffneten Popup stuende der Hover-Hinweis mit demselben Inhalt daneben.
      marker.on('popupopen', () => marker.closeTooltip());
      marker.addTo(this.markerLayer);

      // Fuer das Umfaerben ohne Neuladen (siehe applySubscribed). Ein Turnier kann in mehreren
      // Buendeln stecken — es hat bei Ligen mehrere Spielorte.
      for (const { entry } of group.members) {
        const known = this.groupsByEntry.get(entry.id);
        if (known) known.push(group); else this.groupsByEntry.set(entry.id, [group]);
      }
    }

    this.mixedShown.set(mixed);
  }

  /**
   * In welchen Buendeln ein Turnier steckt — ein Turnier kann mehrere Spielorte haben, und an
   * jedem koennen andere Turniere mit ihm zusammenliegen.
   */
  private readonly groupsByEntry = new Map<string, PinGroup[]>();

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
    // Der Stil gehoert dem BUENDEL, nicht dem einzelnen Turnier: an dem Punkt haengen womoeglich
    // weitere, und „gemerkt" gilt fuer den Punkt, sobald EINES davon gemerkt ist.
    for (const group of this.groupsByEntry.get(entry.id) ?? [])
      group.marker?.setStyle(groupStyle(group));
  }

  /**
   * Der Popup-Inhalt. Steht der Punkt fuer EIN Turnier, ist es unveraendert die gemeinsame
   * Kurzansicht (`TournamentCardComponent`) — dieselbe wie in Liste und Kalender, mit denselben
   * vier Aktionen. Liegen mehrere Turniere auf derselben Koordinate, kommt eine Ebene davor: die
   * LISTE aller, denn sonst waere alles ausser dem obersten unerreichbar.
   *
   * <para>Bewusst zwei Ebenen statt gestapelter Kurzansichten: an einem Punkt koennen ueber
   * hundert Turniere liegen — so viele Komponenten samt Abonnements auf einen Klick hin zu
   * erzeugen waere fuer eine Liste, von der man eines ansieht, nicht zu rechtfertigen. Es gibt
   * darum immer nur EINE Kurzansicht auf einmal.</para>
   *
   * <para>Gebaut wird von Hand, weil Leaflet den Popup-Inhalt in einem eigenen Container
   * ausserhalb dieses Templates haelt. Die Kurzansicht entsteht ueber den ViewContainerRef und
   * bleibt damit Teil der Aenderungserkennung dieser Komponente — nur so aktualisieren sich ihre
   * Symbole nach einem Klick.</para>
   */
  private buildPopup(group: PinGroup): HTMLElement {
    // Leaflet ruft diese Funktion nicht nur beim Oeffnen auf, sondern bei JEDEM `update()` erneut
    // — und `update()` ist genau das, was nach einem Wechsel Liste <-> Kurzansicht noetig ist.
    // Sie muss deshalb den BESTEHENDEN Knoten zurueckgeben: baute sie einen neuen, fiele die
    // Ansicht beim Nachmessen auf die Liste zurueck und riefe sich ueber `resizePopup` endlos
    // selbst auf (im Test als „Maximum call stack size exceeded" aufgeschlagen).
    if (this.popupGroup === group && this.popupHost) return this.popupHost;

    const host = document.createElement('div');
    host.className = 'tm-group';
    this.popupGroup = group;
    this.popupHost = host;

    if (group.members.length === 1) {
      this.destroyPopup();
      host.appendChild(this.buildCard(group.members[0]));
    } else {
      this.renderGroupList(host, group);
    }
    return host;
  }

  /** Die Liste aller Turniere eines Punktes; ein Klick fuehrt zur Kurzansicht des einzelnen. */
  private renderGroupList(host: HTMLElement, group: PinGroup): void {
    // Zurueck aus der Einzelansicht: die dort erzeugte Kurzansicht wird nicht mehr gebraucht.
    this.destroyPopup();
    host.replaceChildren(...MapPopupBuilder.groupList(group, this.text, this.translate.currentLang(),
      member => this.renderGroupCard(host, group, member)));
    this.resizePopup();
  }

  /** Die Kurzansicht EINES Turniers des Buendels, mit dem Weg zurueck zur Liste. */
  private renderGroupCard(host: HTMLElement, group: PinGroup, member: PinMember): void {
    host.replaceChildren(MapPopupBuilder.backButton(group, this.text, () => this.renderGroupList(host, group)));
    host.appendChild(this.buildCard(member));
    this.resizePopup();
  }

  /** Die gemeinsame Kurzansicht als DOM-Element; es gibt immer nur eine offene. */
  private buildCard(member: PinMember): HTMLElement {
    this.destroyPopup();

    const card = this.viewContainer.createComponent(TournamentCardComponent);
    card.setInput('entry', member.entry);
    card.setInput('overview', true);
    card.setInput('venueName', member.spot.name);
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
  /** Das offene Leaflet-Popup — nur, um es nach einem Inhaltswechsel neu vermessen zu lassen. */
  private openPopup?: L.Popup;
  /** Der Knoten des offenen Popups und das Buendel, zu dem er gehoert (siehe `buildPopup`). */
  private popupHost?: HTMLElement;
  private popupGroup?: PinGroup;

  /**
   * Nach dem Umschalten Liste <-> Kurzansicht neu vermessen. Ohne das behaelt Leaflet die
   * Groesse des vorigen Inhalts, und die Kurzansicht steht halb ausserhalb ihrer Kachel.
   * Beim ERSTEN Aufbau ist noch nichts offen — `update()` ist dort ein No-op.
   */
  private resizePopup(): void {
    this.openPopup?.update();
  }

  private destroyPopup(): void {
    this.popup?.destroy();
    this.popup = undefined;
  }

  /** Das Popup ist zu: Knoten und Kurzansicht freigeben, das naechste Oeffnen faengt neu an. */
  private forgetPopup(): void {
    this.destroyPopup();
    this.popupHost = undefined;
    this.popupGroup = undefined;
  }

  /** `instant` genuegt hier: ein Popup gibt es erst nach einem Klick, die Texte stehen laengst. */
  private readonly text: MapText = (key, params) => this.translate.instant(key, params);

  /** Schluessel des Umkreises — `null` ohne Mittelpunkt. Dieselbe Form in `MapView.centre`. */
  private centreKey(): string | null {
    return this.centre ? `${this.centre.lat}|${this.centre.lon}|${this.centre.radiusKm}` : null;
  }

  private applyCentre(): void {
    if (!this.map || !this.radiusLayer) return;
    this.radiusLayer.clearLayers();
    if (!this.centre) { this.lastFitted = null; return; }

    const key = this.centreKey()!;

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

  /** Laeuft gerade Leaflets eigenes Ruecken fuer ein Popup? Siehe `autopanstart`. */
  private autoPanning = false;

  private emitBounds(): void {
    if (!this.map) return;
    if (this.autoPanning) {
      // Nur DIESE eine Bewegung ueberspringen — die naechste echte meldet den Ausschnitt wieder.
      // Der uebersprungene Ausschnitt ist ein paar Dutzend Pixel groesser als der gemeldete; was
      // dort neu hereinragt, kommt beim naechsten Verschieben mit.
      this.autoPanning = false;
      return;
    }
    const b = this.map.getBounds();
    this.boundsChanged.emit(
      `${b.getSouth().toFixed(5)},${b.getWest().toFixed(5)},${b.getNorth().toFixed(5)},${b.getEast().toFixed(5)}`);
    const c = this.map.getCenter();
    const view: MapView = {
      lat: Number(c.lat.toFixed(5)), lon: Number(c.lng.toFixed(5)), zoom: this.map.getZoom(),
      centre: this.centreKey(),
    };
    // Nicht synchron: der Elternteil legt den Ausschnitt in das Feld, das er selbst an
    // `initialView` bindet. Ein neuer Umkreis laeuft aber IN seiner Aenderungserkennung hierher
    // (ngOnChanges 'centre' -> fitBounds ohne Animation -> sofortiges moveend) — die Bindung
    // aenderte sich nach der Pruefung, im Dev-Build ein NG0100.
    queueMicrotask(() => this.viewChanged.emit(view));
  }
}

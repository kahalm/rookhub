import {
  AfterViewInit, ChangeDetectionStrategy, Component, ComponentRef, ElementRef, EventEmitter,
  Input, OnChanges, OnDestroy, Output, SimpleChanges, ViewChild, ViewContainerRef, inject,
} from '@angular/core';
import { TranslateService } from '@ngx-translate/core';
import * as L from 'leaflet';
import { MapPinMarker, pinRadiusFor } from './map-pin-marker';
import { TournamentCardComponent } from './tournament-card.component';
import { DirectoryEntry, DirectoryVenue } from './tournament-directory.model';

/** Sichtbarer Kartenausschnitt als „minLat,minLon,maxLat,maxLon" — Serverformat. */
export type BoundsString = string;

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
 *    Kreis behauptet, der Ort liege in seiner schwer zu treffenden Mitte.
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

    /* Der gebuendelte Punkt: erst die Liste aller Turniere dieses Ortes, dann auf Klick die
       gewohnte Kurzansicht des einzelnen. */
    :host ::ng-deep .tm-group-head { margin: 0; font-size: 0.9rem; font-weight: 600; }

    :host ::ng-deep .tm-group-place,
    :host ::ng-deep .tm-group-vague { margin: 2px 0 0; font-size: 0.78rem; opacity: 0.75; }

    :host ::ng-deep .tm-group-list {
      display: flex;
      flex-direction: column;
      gap: 2px;
      margin-top: 6px;
      /* Ein Punkt kann ueber hundert Turniere tragen — das Popup darf davon nicht zerrissen
         werden. */
      max-height: 38vh;
      overflow-y: auto;
    }

    :host ::ng-deep .tm-group-row {
      display: flex;
      flex-direction: column;
      gap: 1px;
      width: 100%;
      padding: 4px 6px;
      border: 0;
      border-left: 3px solid transparent;
      border-radius: 4px;
      background: none;
      font: inherit;
      text-align: left;
      cursor: pointer;
    }

    :host ::ng-deep .tm-group-row:hover { background: color-mix(in srgb, currentColor 8%, transparent); }

    /* Gemerkte sind auch in der Liste zu erkennen — derselbe Farbton wie beim Pin. */
    :host ::ng-deep .tm-group-row.is-marked { border-left-color: #f9ab00; }

    :host ::ng-deep .tm-group-name {
      font-size: 0.85rem;
      font-weight: 600;
      line-height: 1.25;
      color: var(--mat-sys-primary);
    }

    :host ::ng-deep .tm-group-when { font-size: 0.75rem; opacity: 0.75; }

    :host ::ng-deep .tm-group-back {
      display: block;
      margin-bottom: 4px;
      padding: 0;
      border: 0;
      background: none;
      font: inherit;
      font-size: 0.78rem;
      color: var(--mat-sys-primary);
      cursor: pointer;
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
    this.forgetPopup();
    this.resizeObserver?.disconnect();
    this.map?.remove();
    this.map = undefined;
  }

  private applyEntries(): void {
    if (!this.markerLayer) return;
    // Die Marker verschwinden — mit ihnen das offene Popup, dessen Komponente sonst haengen
    // bleibt.
    this.forgetPopup();
    this.markerLayer.clearLayers();
    this.groupsByEntry.clear();

    for (const group of groupByPoint(this.entries)) {
      const count = group.members.length;
      // Der Kopf waechst mit der Stellenzahl (siehe pinRadiusFor) — damit waechst auch, wie hoch
      // der Pin ueber seinem Ort steht, und Popup und Hinweis muessen das mitmachen.
      const radius = pinRadiusFor(PinRadius, count);
      const above = MapPinMarker.heightAbove(radius);

      const marker = new MapPinMarker([group.lat, group.lon], {
        radius,
        count,
        ...groupStyle(group),
      });
      group.marker = marker;

      // Als FUNKTION, nicht als fertiger Text: der Hinweis wird uebersetzt, und beim Zeichnen der
      // Marker koennen die Sprachdateien noch unterwegs sein.
      marker.bindTooltip(() => this.tooltipHtml(group), { direction: 'top', offset: [0, -above] });
      // Klick = Popup (siehe Klassenkommentar), NICHT der Sprung auf die Detailseite.
      marker.bindPopup(() => this.buildPopup(group),
        { offset: [0, -above + 4], minWidth: 220, maxWidth: 300 });
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
    host.replaceChildren();

    const head = document.createElement('p');
    head.className = 'tm-group-head';
    head.textContent =
      this.text('tournamentDirectory.map.samePoint', { count: group.members.length });
    host.appendChild(head);

    const place = placeOf(group);
    if (place) {
      const where = document.createElement('p');
      where.className = 'tm-group-place';
      where.textContent = place;
      host.appendChild(where);
    }

    // Warum sie uebereinanderliegen, sagt sich nur bei der einen Ursache, die wir kennen: alle
    // Spielorte dieses Punktes sind Regionsmitten. Ohne diesen Satz sieht es nach einem Fehler
    // aus, obwohl es die ehrliche Wiedergabe eines Ortstextes ist, der nur „Tirol" sagt.
    if (isVague(group)) {
      const why = document.createElement('p');
      why.className = 'tm-group-vague';
      why.textContent = this.text('tournamentDirectory.map.samePointVague');
      host.appendChild(why);
    }

    const list = document.createElement('div');
    list.className = 'tm-group-list';
    for (const member of group.members) {
      const row = document.createElement('button');
      row.type = 'button';
      row.className = 'tm-group-row';
      if (member.entry.subscribed) row.classList.add('is-marked');

      const name = document.createElement('span');
      name.className = 'tm-group-name';
      // textContent, nicht innerHTML: die Namen kommen von chess-results, also von aussen.
      name.textContent = member.entry.name;
      const when = document.createElement('span');
      when.className = 'tm-group-when';
      when.textContent = dateRange(member.entry);

      row.append(name, when);
      row.addEventListener('click', () => this.renderGroupCard(host, group, member));
      list.appendChild(row);
    }
    host.appendChild(list);
    this.resizePopup();
  }

  /** Die Kurzansicht EINES Turniers des Buendels, mit dem Weg zurueck zur Liste. */
  private renderGroupCard(host: HTMLElement, group: PinGroup, member: PinMember): void {
    host.replaceChildren();

    const back = document.createElement('button');
    back.type = 'button';
    back.className = 'tm-group-back';
    back.textContent =
      this.text('tournamentDirectory.map.backToList', { count: group.members.length });
    back.addEventListener('click', () => this.renderGroupList(host, group));
    host.appendChild(back);

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

  /**
   * Der Hinweis am Pin. Bei einem Buendel nennt er die Anzahl statt eines der Turniere — ein
   * herausgegriffener Name behauptete, der Punkt gehoere ihm.
   */
  private tooltipHtml(group: PinGroup): string {
    if (group.members.length > 1) {
      const place = placeOf(group);
      return `<strong>${escapeHtml(
        this.text('tournamentDirectory.map.samePoint', { count: group.members.length }))}</strong>` +
        (place ? `<br>${escapeHtml(place)}` : '');
    }
    const { entry, spot } = group.members[0];
    const place = entry.venues.length > 1 ? spot.name : (entry.location ?? '');
    return `<strong>${escapeHtml(entry.name)}</strong><br>${escapeHtml(dateRange(entry))}` +
           (place ? `<br>${escapeHtml(place)}` : '');
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
 * Ein Punkt und alles, was auf ihm liegt. Der Pin gehoert der KOORDINATE, nicht dem Turnier —
 * zwei Turniere mit demselben Ortstext („Tirol") sitzen auf derselben Landesmitte, und der
 * zweite Pin waere unter dem ersten unerreichbar.
 */
interface PinGroup {
  lat: number;
  lon: number;
  members: PinMember[];
  marker?: MapPinMarker;
}

/** Ein Turnier an diesem Punkt samt dem Spielort, mit dem es hierher kam. */
interface PinMember {
  entry: DirectoryEntry;
  spot: DirectoryVenue;
}

/**
 * Die zu zeichnenden Punkte, gebuendelt nach Koordinate und in Zeichenreihenfolge.
 *
 * <p>Gebuendelt wird bei EXAKT gleicher Koordinate (auf fuenf Nachkommastellen, gut einen Meter),
 * nicht bei „nah beieinander": ein Abstandsmass haenge am Zoom und wuerde zwei verschiedene Orte
 * zu einem erklaeren, sobald man weit genug herauszoomt. Gleiche Koordinate heisst dagegen
 * nachweislich dieselbe Aussage.</p>
 *
 * <p>Gemerkte ZULETZT: Leaflet zeichnet in Reihenfolge des Hinzufuegens, und ein gemerkter Punkt
 * soll nicht unter einem beliebigen anderen liegen. In einer Stadt mit dreissig Turnieren ist
 * genau das der Unterschied zwischen „ich sehe meins" und „ich suche meins". `sort` ist stabil,
 * die Reihenfolge des Servers bleibt innerhalb der beiden Gruppen also erhalten.</p>
 */
function groupByPoint(entries: DirectoryEntry[]): PinGroup[] {
  const byPoint = new Map<string, PinGroup>();
  for (const entry of entries) {
    // Ein Punkt JE SPIELORT: bei Ligen nennt chess-results mehrere („Mayrhofen, St.Veit").
    // Mit nur dem Hauptort verschwaende ein Turnier die Haelfte seiner Orte, und die
    // Umkreissuche fand es nicht, obwohl es zur Haelfte vor der Haustuer stattfindet.
    for (const spot of venuesOf(entry)) {
      const key = `${spot.lat.toFixed(5)}|${spot.lon.toFixed(5)}`;
      const group = byPoint.get(key);
      if (group) group.members.push({ entry, spot });
      else byPoint.set(key, { lat: spot.lat, lon: spot.lon, members: [{ entry, spot }] });
    }
  }
  return [...byPoint.values()]
    .sort((a, b) => Number(hasSubscribed(a)) - Number(hasSubscribed(b)));
}

/** Gemerkt ist ein Punkt, sobald EINES seiner Turniere gemerkt ist — sonst ginge es unter. */
function hasSubscribed(group: PinGroup): boolean {
  return group.members.some(m => m.entry.subscribed);
}

/** Nur ungefaehr verortet ist ein Punkt, wenn ALLE seine Spielorte Regionsmitten sind. */
function isVague(group: PinGroup): boolean {
  return group.members.every(m => m.spot.geoSource === 'Region');
}

/** Der Ortsname des Punktes; alle seine Spielorte liegen ja auf derselben Koordinate. */
function placeOf(group: PinGroup): string {
  const { entry, spot } = group.members[0];
  return spot.name || entry.geoPlaceName || entry.location || '';
}

function groupStyle(group: PinGroup): L.PathOptions {
  return pinStyle(hasSubscribed(group), isVague(group));
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
 *
 * <p>Die dritte Angabe — wie VIELE Turniere auf dem Punkt liegen — traegt der Pin als Zahl im
 * Kopf (siehe `MapPinMarker`), nicht als weitere Farbe: eine Anzahl ist eine Anzahl.</p>
 */
function pinStyle(subscribed: boolean, vague: boolean): L.PathOptions {
  if (subscribed) {
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

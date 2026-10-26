import { ComponentRef, ViewContainerRef } from '@angular/core';
import type * as L from 'leaflet';
import { PinGroup, PinMember, placeName } from './map-pin-groups';
import { MapPopupBuilder, MapText } from './map-popup-builder';
import { TournamentCardComponent } from './tournament-card.component';
import { DirectoryEntry, DirectoryIgnoredChange } from './tournament-directory.model';

/** Was die Kurzansicht im Popup nach draussen meldet — die Karte reicht es weiter. */
export interface MapPopupEvents {
  selected(entry: DirectoryEntry): void;
  ignored(change: DirectoryIgnoredChange): void;
  subscribed(entry: DirectoryEntry, subscribed: boolean): void;
}

/**
 * Der Inhalt des Karten-Popups und sein Lebenszyklus. Steht der Punkt fuer EIN Turnier, ist es
 * unveraendert die gemeinsame Kurzansicht (`TournamentCardComponent`) — dieselbe wie in Liste und
 * Kalender, mit denselben vier Aktionen. Liegen mehrere Turniere auf derselben Koordinate, kommt
 * eine Ebene davor: die LISTE aller, denn sonst waere alles ausser dem obersten unerreichbar.
 *
 * <p>Bewusst zwei Ebenen statt gestapelter Kurzansichten: an einem Punkt koennen ueber hundert
 * Turniere liegen — so viele Komponenten samt Abonnements auf einen Klick hin zu erzeugen waere
 * fuer eine Liste, von der man eines ansieht, nicht zu rechtfertigen. Es gibt darum immer nur EINE
 * Kurzansicht auf einmal.</p>
 *
 * <p>Gebaut wird von Hand, weil Leaflet den Popup-Inhalt in einem eigenen Container ausserhalb des
 * Templates der Karte haelt. Die Kurzansicht entsteht ueber den ViewContainerRef der KARTE und
 * bleibt damit Teil ihrer Aenderungserkennung — nur so aktualisieren sich ihre Symbole nach einem
 * Klick. Herausgezogen aus `TournamentMapComponent` (Zerlegung Z27), Verhalten unveraendert.</p>
 */
export class MapPopupContent {
  private card?: ComponentRef<TournamentCardComponent>;
  /** Das offene Leaflet-Popup — nur, um es nach einem Inhaltswechsel neu vermessen zu lassen. */
  private openPopup?: L.Popup;
  /** Der Knoten des offenen Popups und das Buendel, zu dem er gehoert (siehe `build`). */
  private host?: HTMLElement;
  private group?: PinGroup;

  constructor(
    private readonly viewContainer: ViewContainerRef,
    private readonly text: MapText,
    private readonly locale: () => string | null,
    private readonly events: MapPopupEvents,
  ) {}

  /** Der Popup-Inhalt eines Punktes — als Funktion an `bindPopup` gebunden. */
  build(group: PinGroup): HTMLElement {
    // Leaflet ruft diese Funktion nicht nur beim Oeffnen auf, sondern bei JEDEM `update()` erneut
    // — und `update()` ist genau das, was nach einem Wechsel Liste <-> Kurzansicht noetig ist.
    // Sie muss deshalb den BESTEHENDEN Knoten zurueckgeben: baute sie einen neuen, fiele die
    // Ansicht beim Nachmessen auf die Liste zurueck und riefe sich ueber `resize` endlos
    // selbst auf (im Test als „Maximum call stack size exceeded" aufgeschlagen).
    if (this.group === group && this.host) return this.host;

    const host = document.createElement('div');
    host.className = 'tm-group';
    this.group = group;
    this.host = host;

    if (group.members.length === 1) {
      this.destroyCard();
      host.appendChild(this.buildCard(group.members[0]));
    } else {
      this.renderList(host, group);
    }
    return host;
  }

  /** Leaflet hat ein Popup geoeffnet (`popupopen`). */
  opened(popup: L.Popup): void {
    this.openPopup = popup;
  }

  /** Leaflet hat das Popup geschlossen (`popupclose`). */
  closed(): void {
    this.openPopup = undefined;
    this.forget();
  }

  /** Das Popup ist zu: Knoten und Kurzansicht freigeben, das naechste Oeffnen faengt neu an. */
  forget(): void {
    this.destroyCard();
    this.host = undefined;
    this.group = undefined;
  }

  /** Die Liste aller Turniere eines Punktes; ein Klick fuehrt zur Kurzansicht des einzelnen. */
  private renderList(host: HTMLElement, group: PinGroup): void {
    // Zurueck aus der Einzelansicht: die dort erzeugte Kurzansicht wird nicht mehr gebraucht.
    this.destroyCard();
    host.replaceChildren(...MapPopupBuilder.groupList(group, this.text, this.locale(),
      member => this.renderCard(host, group, member)));
    this.resize();
  }

  /** Die Kurzansicht EINES Turniers des Buendels, mit dem Weg zurueck zur Liste. */
  private renderCard(host: HTMLElement, group: PinGroup, member: PinMember): void {
    host.replaceChildren(MapPopupBuilder.backButton(group, this.text, () => this.renderList(host, group)));
    host.appendChild(this.buildCard(member));
    this.resize();
  }

  /** Die gemeinsame Kurzansicht als DOM-Element; es gibt immer nur eine offene. */
  private buildCard(member: PinMember): HTMLElement {
    this.destroyCard();

    const card = this.viewContainer.createComponent(TournamentCardComponent);
    card.setInput('entry', member.entry);
    card.setInput('overview', true);
    card.setInput('venueName', placeName(member.spot.name));
    card.instance.selected.subscribe(selected => this.events.selected(selected));
    card.instance.ignoredChanged.subscribe(change => this.events.ignored(change));
    // Nur umfaerben, nicht neu laden — sonst schlaegt das gerade offene Popup zu.
    card.instance.subscribedChanged.subscribe(
      change => this.events.subscribed(change.entry, change.subscribed));
    // Sofort rendern: Leaflet erwartet ein FERTIGES Element und misst danach die Popup-Groesse.
    card.changeDetectorRef.detectChanges();

    this.card = card;
    return card.location.nativeElement as HTMLElement;
  }

  /**
   * Nach dem Umschalten Liste <-> Kurzansicht neu vermessen. Ohne das behaelt Leaflet die
   * Groesse des vorigen Inhalts, und die Kurzansicht steht halb ausserhalb ihrer Kachel.
   * Beim ERSTEN Aufbau ist noch nichts offen — `update()` ist dort ein No-op.
   */
  private resize(): void {
    this.openPopup?.update();
  }

  private destroyCard(): void {
    this.card?.destroy();
    this.card = undefined;
  }
}

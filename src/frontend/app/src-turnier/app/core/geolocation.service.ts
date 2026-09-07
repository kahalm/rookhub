import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';

/**
 * Warum der Standort nicht zu haben war. Die Unterscheidung ist der ganze Zweck dieses Typs: „du
 * hast das abgelehnt" braucht eine andere Antwort in der Oberflaeche als „dein Geraet weiss es
 * gerade nicht" — im ersten Fall hilft nur die Browser-Einstellung, im zweiten ein zweiter
 * Versuch.
 */
export type GeolocationFailure = 'unsupported' | 'denied' | 'unavailable' | 'timeout';

export interface GeolocationFix {
  lat: number;
  lon: number;
  /** Genauigkeit in Metern, wie der Browser sie meldet. */
  accuracyM: number;
}

/**
 * Huelle um `navigator.geolocation`.
 *
 * <p>Sie existiert aus zwei Gruenden. Erstens ist die Browser-API rueckruf-basiert und liegt
 * ausserhalb der Angular-Zone; als Observable eingepackt haengt sie sich in dieselben Ketten wie
 * jeder andere Datenweg. Zweitens — und das ist der eigentliche Grund — ist sie in Tests nicht
 * ersetzbar, solange sie mitten in einer Komponente steht: `navigator.geolocation` ist
 * schreibgeschuetzt, und ein echter Aufruf im Testlauf oeffnet eine Berechtigungsabfrage, die
 * niemand beantwortet.</p>
 *
 * <p>Der Standort wird NIE von selbst abgefragt. Der Nutzer drueckt darauf; das ist der
 * Unterschied zwischen einem Werkzeug und einer Ueberwachung — und ausserdem loest jede
 * unaufgeforderte Abfrage beim ersten Seitenaufruf eine Berechtigungsfrage aus, die dann ohne
 * erkennbaren Anlass dasteht und im Zweifel abgelehnt wird.</p>
 */
@Injectable({ providedIn: 'root' })
export class GeolocationService {
  /** Nach so vielen Millisekunden gilt der Versuch als gescheitert. */
  private static readonly TimeoutMs = 10_000;

  get supported(): boolean {
    return typeof navigator !== 'undefined' && !!navigator.geolocation;
  }

  /**
   * Einen Standort holen. Bricht mit einem <see cref="GeolocationFailure"/> als Fehlerwert ab —
   * kein `GeolocationPositionError` nach draussen: dessen Zahlencodes muesste sonst jeder
   * Aufrufer selbst deuten.
   */
  current(): Observable<GeolocationFix> {
    return new Observable<GeolocationFix>(subscriber => {
      if (!this.supported) {
        subscriber.error('unsupported' as GeolocationFailure);
        return;
      }

      navigator.geolocation.getCurrentPosition(
        position => {
          subscriber.next({
            lat: position.coords.latitude,
            lon: position.coords.longitude,
            accuracyM: position.coords.accuracy,
          });
          subscriber.complete();
        },
        error => subscriber.error(failureOf(error)),
        {
          // Kein `enableHighAccuracy`: fuer eine Umkreissuche ueber 100 km ist das Netzwerk-Ortung
          // genau genug, und GPS kostet auf dem Handy Sekunden und Akku fuer einen Unterschied,
          // den niemand sieht.
          enableHighAccuracy: false,
          timeout: GeolocationService.TimeoutMs,
          // Eine Minute alte Ortung ist fuer diesen Zweck taufrisch — und kommt sofort.
          maximumAge: 60_000,
        },
      );
    });
  }
}

function failureOf(error: GeolocationPositionError): GeolocationFailure {
  switch (error.code) {
    case 1: return 'denied';          // PERMISSION_DENIED
    case 3: return 'timeout';         // TIMEOUT
    default: return 'unavailable';    // POSITION_UNAVAILABLE
  }
}

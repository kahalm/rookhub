import { HttpEventType, HttpInterceptorFn } from '@angular/common/http';
import { ApplicationRef, inject } from '@angular/core';
import { tap } from 'rxjs';

/**
 * Zeichnet nach JEDER abgeschlossenen HTTP-Antwort einmal neu.
 *
 * <p><b>Das Problem, das hier ein Ende findet.</b> Eine Antwort trifft ausserhalb der
 * Angular-Zone ein; eine Feldzuweisung im Abonnenten loeste darum keine Aenderungserkennung aus,
 * und das Ergebnis stand erst da, wenn irgendein anderes Ereignis einen Lauf ansties — ein Klick,
 * ein Tastendruck, das Speichern. Gemeldet wurde es zuletzt an der Spielersuche im Profil („er
 * sagt FIDE-ID uebernommen, aber im Feld steht nichts, bis ich speichere"), davor am
 * Turnierkalender („ich sehe das Ergebnis erst, wenn ich von Karte auf Liste wechsle").</p>
 *
 * <p><b>Warum zentral statt je Seite.</b> Bisher war die Antwort „leg alles aus HTTP in Signale" —
 * richtig, aber sie muss auf JEDER Seite eingehalten werden und ist genau dort nicht zu sehen, wo
 * sie fehlt. Zusaetzlich reicht sie nicht ueberall: die Spielersuche aendert ein Objekt, das der
 * Aufrufer besitzt (in einem Signal, aber DARIN) — ein Signal meldet eine Mutation nicht, weil die
 * Referenz dieselbe bleibt. Ein Aenderungslauf nach der Antwort deckt beide Faelle ab und macht
 * die bestehenden Signal-Loesungen nicht falsch.</p>
 *
 * <p><b>Drei Feinheiten.</b> (1) Der Lauf wird als Mikrotask nachgestellt, damit der ABONNENT
 * zuerst seine Felder setzt — synchron im `tap` waere er zu frueh. (2) Nur die fertige Antwort
 * zaehlt, nicht jedes Fortschritts-Ereignis: der ndjson-Strom der externen Engine liefert
 * hunderte davon, und seine Anzeige haengt ohnehin an einem Subject mit `async`-Pipe. (3) Ein
 * Fehlschlag zaehlt genauso — auch eine Fehlerbehandlung setzt Felder.</p>
 */
/**
 * Wer hat fuer den naechsten Mikrotask schon einen Lauf eingeplant? Der Interceptor laeuft je
 * ANFRAGE neu, eine Variable in ihm koennte also nichts buendeln — ein Dashboard mit fuenf
 * gleichzeitigen Abrufen loeste sonst fuenf Laeufe aus. Der Zustand haengt an der ANWENDUNG (und
 * nicht am Modul), damit zwei parallele Test-Umgebungen sich nicht gegenseitig den Lauf nehmen;
 * das WeakSet haelt dabei nichts am Leben.
 */
const pending = new WeakSet<ApplicationRef>();

export const renderAfterHttpInterceptor: HttpInterceptorFn = (req, next) => {
  const appRef = inject(ApplicationRef);

  const render = () => {
    if (pending.has(appRef)) return;
    pending.add(appRef);
    queueMicrotask(() => {
      pending.delete(appRef);
      try {
        appRef.tick();
      } catch {
        // Laeuft die Anwendung gerade schon durch einen Aenderungslauf oder ist sie zerstoert,
        // ist der zusaetzliche Lauf unnoetig — aber niemals ein Grund, den Aufrufer scheitern zu
        // lassen: der hat seine Antwort laengst.
      }
    });
  };

  return next(req).pipe(tap({
    next: event => { if (event.type === HttpEventType.Response) render(); },
    error: () => render(),
  }));
};

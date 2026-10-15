import { Injectable, Provider, signal } from '@angular/core';
import { MAT_RIPPLE_GLOBAL_OPTIONS, MATERIAL_ANIMATIONS, RippleGlobalOptions } from '@angular/material/core';
import { deviceLooksEink, isEinkActive, isEinkInitialized, onEinkChange, readEinkChoice, resolveEink, setEinkActive, writeEinkChoice } from './eink';

/**
 * E-Ink-Modus als Dienst (Profil-Schalter, Signal für Komponenten). Der Zustand selbst liegt in `eink.ts`
 * (früh in main.ts gesetzt); der Dienst spiegelt ihn als Signal und speichert die Wahl JE GERÄT.
 */
@Injectable({ providedIn: 'root' })
export class EinkService {
  private readonly _enabled = signal(isEinkActive());
  /** Ist der Modus gerade an? */
  readonly enabled = this._enabled.asReadonly();
  /** Hat dieses Gerät eine eigene Wahl gespeichert (sonst gilt die Media-Query)? */
  readonly hasChoice = signal(readEinkChoice() !== null);
  /** Meldet das Gerät selbst einen E-Ink-Bildschirm (Vorgabe ohne Wahl)? */
  readonly deviceDefault = deviceLooksEink();

  constructor() {
    onEinkChange(on => this._enabled.set(on));
    // Falls main.ts den Zustand nicht gesetzt hat (Tests, andere Einstiege): jetzt auflösen.
    if (!isEinkInitialized()) setEinkActive(resolveEink());
  }

  /** Ein-/Ausschalten, sofort wirksam, für dieses Gerät gespeichert. Gibt zurück, ob das Speichern geklappt hat. */
  set(on: boolean): boolean {
    const saved = writeEinkChoice(on);
    this.hasChoice.set(saved);
    setEinkActive(on);
    return saved;
  }
}

/**
 * Material liest beide Einstellungen beim Auslösen bzw. beim Anlegen einer Komponente — als Getter wirken sie, sobald
 * der Modus umgeschaltet ist: keine Ripples, keine Material-Animationen (Dialoge/Menüs öffnen ohne auf `animationend`
 * zu warten, das es unter `animation: none` nie gäbe).
 */
export function provideEinkMaterial(): Provider[] {
  const ripple: RippleGlobalOptions = { get disabled() { return isEinkActive(); } };
  return [
    { provide: MAT_RIPPLE_GLOBAL_OPTIONS, useValue: ripple },
    { provide: MATERIAL_ANIMATIONS, useValue: { get animationsDisabled() { return isEinkActive(); } } },
  ];
}

import { Injectable, signal } from '@angular/core';
import {
  UI_SCALE_STEPS, UiScaleDiagnostics, applyUiScale, currentUiScale, currentUiScaleMode, onUiScaleChange, uiScaleDiagnostics,
  writeUiScale,
} from './ui-scale';

/**
 * UI-Skalierung als Dienst (Profil-Auswahl, Signal). Mechanik und früher Start in `ui-scale.ts` (main.ts); hier nur
 * Wahl speichern JE GERÄT und sofort anwenden (docs/DESIGN.md 3.7).
 */
@Injectable({ providedIn: 'root' })
export class UiScaleService {
  readonly steps = UI_SCALE_STEPS;
  private readonly _scale = signal(currentUiScale());
  /** Aktuelle Stufe in Prozent (100 = normal). */
  readonly scale = this._scale.asReadonly();
  /** Kann dieser Browser nicht sicher skalieren (alter Desktop-Browser)? Dann bleibt die Seite bei 100 %. */
  readonly unsupported = signal(currentUiScaleMode() === 'none');
  /** Befund zur Fehlersuche am Gerät (Weg, Breiten) — in der Profilkarte angezeigt. */
  readonly diagnostics = signal<UiScaleDiagnostics>(uiScaleDiagnostics());

  constructor() {
    onUiScaleChange(pct => {
      this._scale.set(pct);
      this.unsupported.set(currentUiScaleMode() === 'none');
      this.diagnostics.set(uiScaleDiagnostics());
    });
  }

  /** Befund neu messen (beim Öffnen der Profilkarte). */
  refreshDiagnostics(): void { this.diagnostics.set(uiScaleDiagnostics()); }

  /** Stufe wählen: sofort wirksam, für dieses Gerät gespeichert. Gibt zurück, ob das Speichern geklappt hat. */
  set(pct: number): boolean {
    const saved = writeUiScale(pct);
    applyUiScale(pct);
    return saved;
  }
}

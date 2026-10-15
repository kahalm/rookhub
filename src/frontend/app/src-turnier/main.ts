import { bootstrapApplication } from '@angular/platform-browser';
import { applyUiScaleEarly } from '@rh/core/ui-scale';
import { applyEinkEarly } from '@rh/core/eink';
import { turnierConfig } from './app/app.config';
import { TurnierAppComponent } from './app/app.component';

// E-Ink-Modus vor dem ersten Bild setzen (kein Aufblitzen des Dunkelmodus, docs/DESIGN.md 3.6).
applyEinkEarly();
// UI-Skalierung je Gerät vor dem ersten Bild (Meta-Viewport bzw. zoom, docs/DESIGN.md 3.7) — sonst springt das Layout.
applyUiScaleEarly();

bootstrapApplication(TurnierAppComponent, turnierConfig)
  .catch((err) => console.error(err));

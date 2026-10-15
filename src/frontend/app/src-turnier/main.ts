import { bootstrapApplication } from '@angular/platform-browser';
import { applyEinkEarly } from '@rh/core/eink';
import { turnierConfig } from './app/app.config';
import { TurnierAppComponent } from './app/app.component';

// E-Ink-Modus vor dem ersten Bild setzen (kein Aufblitzen des Dunkelmodus, docs/DESIGN.md 3.6).
applyEinkEarly();

bootstrapApplication(TurnierAppComponent, turnierConfig)
  .catch((err) => console.error(err));

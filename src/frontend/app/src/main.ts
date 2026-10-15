import { bootstrapApplication } from '@angular/platform-browser';
import { applyEinkEarly } from './app/core/eink';
import { appConfig } from './app/app.config';
import { AppComponent } from './app/app.component';

// E-Ink-Modus vor dem ersten Bild setzen (kein Aufblitzen des Dunkelmodus, docs/DESIGN.md 3.6).
applyEinkEarly();

bootstrapApplication(AppComponent, appConfig)
  .catch((err) => console.error(err));

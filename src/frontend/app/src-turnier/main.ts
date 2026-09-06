import { bootstrapApplication } from '@angular/platform-browser';
import { turnierConfig } from './app/app.config';
import { TurnierAppComponent } from './app/app.component';

bootstrapApplication(TurnierAppComponent, turnierConfig)
  .catch((err) => console.error(err));

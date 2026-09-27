import { bootstrapApplication } from '@angular/platform-browser';
import { kidhubConfig } from './app/app.config';
import { KidHubAppComponent } from './app/app.component';

bootstrapApplication(KidHubAppComponent, kidhubConfig)
  .catch((err) => console.error(err));

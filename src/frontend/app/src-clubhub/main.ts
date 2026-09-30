import { bootstrapApplication } from '@angular/platform-browser';
import { clubhubConfig } from './app/app.config';
import { ClubHubAppComponent } from './app/app.component';

bootstrapApplication(ClubHubAppComponent, clubhubConfig)
  .catch((err) => console.error(err));

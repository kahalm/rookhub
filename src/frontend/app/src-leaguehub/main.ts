import { bootstrapApplication } from '@angular/platform-browser';
import { leaguehubConfig } from './app/app.config';
import { LeagueHubAppComponent } from './app/app.component';

bootstrapApplication(LeagueHubAppComponent, leaguehubConfig)
  .catch((err) => console.error(err));

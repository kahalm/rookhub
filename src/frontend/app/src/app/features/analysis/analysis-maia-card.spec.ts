import { NO_ERRORS_SCHEMA } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { ActivatedRoute, Router } from '@angular/router';
import { MatDialog } from '@angular/material/dialog';
import { provideTranslateService } from '@ngx-translate/core';
import { Subject, of } from 'rxjs';
import { AnalysisComponent } from './analysis.component';
import { AnalysisEngineService } from './analysis-engine.service';
import { AnalysisBoardComponent } from './analysis-board.component';
import { PositionSetupComponent } from './position-setup.component';
import { PositionRepertoiresComponent } from '../repertoire/position-repertoires.component';
import { OpeningExplorerComponent } from './opening-explorer.component';
import { AnalysisMoveTreeComponent } from './analysis-move-tree.component';
import { ExternalEngineService } from './external-engine.service';
import { AnalysisHistoryService } from './analysis-history.service';
import { SnackbarService } from '../../core/snackbar.service';
import { AuthService } from '../../core/auth.service';
import { MaiaEngineService } from './maia/maia-engine.service';
import { GamesService } from '../games/games.service';
import { AnalyzeGameService } from '../games/analyze-game.service';

/**
 * Karte „Sparring gegen Maia" im Analysebrett (0.760.0): standardmäßig zu, das ⋮ der Stellung blendet sie ein, ✕
 * wieder aus; läuft ein Sparring, bleibt sie. Eigene Spec-Datei, damit `analysis.component.spec.ts` (> 2 000 Zeilen)
 * nicht weiter wächst. ⋮-Menü und Karte sind hier ECHT — geklickt wird wie vom Nutzer.
 */
describe('AnalysisComponent Karte „Sparring gegen Maia" (0.760.0)', () => {
  let fixture: ComponentFixture<AnalysisComponent>;
  const host = () => fixture.nativeElement as HTMLElement;
  const card = () => host().querySelector('app-maia-sparring-card');

  beforeEach(async () => {
    const engine: any = {
      analysis$: new Subject(), engineFatalError$: new Subject(), remoteFallback$: new Subject(), remoteInterrupted$: new Subject(),
      setMultiPv: () => {}, setDepth: () => {}, setRemoteEngine: () => {},
      analyze: () => Promise.resolve(), stop: () => {}, destroy: () => {},
    };
    const maia = {
      chooseMove: () => Promise.resolve(null), release: () => {}, status: () => 'idle', prepare: () => Promise.resolve(false),
      progress: () => 0, error: () => null, canStore: () => true,
    };
    TestBed.overrideComponent(AnalysisComponent, {
      remove: { imports: [AnalysisBoardComponent, PositionSetupComponent, PositionRepertoiresComponent,
        OpeningExplorerComponent, AnalysisMoveTreeComponent] },
      add: { schemas: [NO_ERRORS_SCHEMA] },
    });
    await TestBed.configureTestingModule({
      imports: [AnalysisComponent],
      providers: [
        provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
        { provide: AnalysisEngineService, useValue: engine },
        { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: { get: () => null } } } },
        { provide: Router, useValue: { navigateByUrl: () => {} } },
        { provide: SnackbarService, useValue: { show: () => {}, warn: () => {} } },
        { provide: AuthService, useValue: { isLoggedIn: false, has: () => false } },
        { provide: ExternalEngineService, useValue: { listEngines: () => new Subject(), analyse: () => {} } },
        { provide: AnalysisHistoryService, useValue: { save: () => of({}), get: () => of({}), list: () => of([]) } },
        { provide: MatDialog, useValue: { open: () => {} } },
        { provide: MaiaEngineService, useValue: maia },
        { provide: GamesService, useValue: { importPgn: () => of({ ids: [] }), analyzeUrl: (id: number) => `/api/games/${id}/analyze` } },
        { provide: AnalyzeGameService, useValue: { status: () => of(null), submit: () => of(false) } },
      ],
    }).compileComponents();
    fixture = TestBed.createComponent(AnalysisComponent);
    fixture.detectChanges();   // kein whenStable: die Seite hält eigene Zeitgeber offen
  });

  afterEach(() => {
    fixture.destroy();
    document.querySelectorAll('.cdk-overlay-container').forEach(c => c.innerHTML = '');
  });

  function openViaMenu(): void {
    (host().querySelector('app-position-menu .position-menu-btn') as HTMLButtonElement).click();
    fixture.detectChanges();
    const entry = document.querySelector('.cdk-overlay-container .pm-sparring') as HTMLElement | null;
    expect(entry).withContext('Eintrag im ⋮').not.toBeNull();
    entry!.click();
    fixture.detectChanges();
  }

  it('ist standardmäßig nicht eingeblendet', () => {
    expect(card()).toBeNull();
  });

  it('der ⋮-Eintrag blendet die Karte ein, ✕ wieder aus', () => {
    openViaMenu();
    expect(card()).not.toBeNull();

    (card()!.querySelector('button.maia-close') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(card()).toBeNull();
  });

  it('läuft ein Sparring, ist die Karte immer da — ohne ✕, und closeMaiaCard lässt sie stehen', () => {
    const c: any = fixture.componentInstance;
    c.spar.sparring = { start: c.root, tip: c.root, userColor: 'white', engineWasOn: true };
    fixture.detectChanges();
    expect(card()).not.toBeNull();
    expect(card()!.querySelector('button.maia-close')).toBeNull();

    c.closeMaiaCard();
    fixture.detectChanges();
    expect(card()).not.toBeNull();
  });

  it('im Stellungs-Editor bleibt die Karte weg, auch wenn sie offen war', () => {
    const c: any = fixture.componentInstance;
    c.openMaiaCard();
    c.editing = true;
    fixture.detectChanges();
    expect(card()).toBeNull();
  });
});

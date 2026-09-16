import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { RepertoireDetailComponent } from './repertoire-detail.component';

describe('RepertoireDetailComponent', () => {
  it('creates (template AOT-compiles + DI resolves)', async () => {
    await TestBed.configureTestingModule({
      imports: [RepertoireDetailComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(RepertoireDetailComponent);
    expect(fixture.componentInstance).toBeTruthy();
  });
});

describe('RepertoireDetailComponent Linien-Download', () => {
  const PGN = '[Event "Rep"]\n[White "1A | 2.Sf3"]\n[Black "1) Kapitel"]\n[Result "*"]\n\n'
    + '1. e4 e6 {[%cal Gd7d5][%alt c5 e5]Französisch} 2. Nf3 d5 3. e5 (3. Nc3 Nf6 {Steinitz}) c5 *\n';

  async function make() {
    await TestBed.configureTestingModule({
      imports: [RepertoireDetailComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
        provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }),
      ],
    }).compileComponents();
    const comp = TestBed.createComponent(RepertoireDetailComponent).componentInstance;
    comp.repertoire = { id: 13, name: 'Martinović Französisch' } as any;
    comp.viewerService.loadPgn(PGN);
    let saved: Blob | undefined;
    spyOn(URL, 'createObjectURL').and.callFake((b: Blob | MediaSource) => { saved = b as Blob; return 'blob:test'; });
    spyOn(URL, 'revokeObjectURL');
    const click = spyOn(HTMLAnchorElement.prototype, 'click');
    return { comp, click, text: () => saved!.text() };
  }

  it('speichert den Originaltext der Linie — mit Varianten, ohne [%alt]', async () => {
    const { comp, click, text } = await make();

    comp.onDownloadLine(comp.viewerService.lines[0]);

    expect(await text()).toBe('[Event "Rep"]\n[White "1A | 2.Sf3"]\n[Black "1) Kapitel"]\n[Result "*"]\n\n'
      + '1. e4 e6 {[%cal Gd7d5]Französisch} 2. Nf3 d5 3. e5 (3. Nc3 Nf6 {Steinitz}) c5 *\n');
    expect((click.calls.mostRecent().object as HTMLAnchorElement).download)
      .toBe('Martinović_Französisch_1_Kapitel_1A_2_Sf3.pgn');
  });

  it('ohne Originaltext baut sie die Linie aus den geparsten Zügen', async () => {
    const { comp, text } = await make();
    comp.viewerService.rawGames = [];

    comp.onDownloadLine(comp.viewerService.lines[0]);

    const pgn = await text();
    expect(pgn).toContain('1. e4 e6');
    expect(pgn).not.toContain('[%alt');
  });
});

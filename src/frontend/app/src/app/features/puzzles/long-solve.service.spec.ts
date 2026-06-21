import { of } from 'rxjs';
import { LongSolveService } from './long-solve.service';

describe('LongSolveService', () => {
  function make(dialogResult?: boolean) {
    const open = jasmine.createSpy('open').and.returnValue({ afterClosed: () => of(dialogResult) });
    return { svc: new LongSolveService({ open } as any), open };
  }

  it('gibt kurze Zeiten (≤ Schwellwert) unverändert zurück — ohne Nachfrage', (done) => {
    const { svc, open } = make();
    svc.resolve(120).subscribe(sec => {
      expect(sec).toBe(120);
      expect(open).not.toHaveBeenCalled();
      done();
    });
  });

  it('fragt bei langer Zeit nach und kappt bei „war weg" auf den Schwellwert', (done) => {
    const { svc, open } = make(false);
    svc.resolve(900).subscribe(sec => {
      expect(open).toHaveBeenCalled();
      expect(sec).toBe(LongSolveService.THRESHOLD_SECONDS);
      done();
    });
  });

  it('übernimmt die volle Zeit bei „ja so lange"', (done) => {
    const { svc } = make(true);
    svc.resolve(900).subscribe(sec => { expect(sec).toBe(900); done(); });
  });
});

import { Injector, runInInjectionContext } from '@angular/core';
import { injectOptional } from './inject-optional';

class Present { readonly tag = 'da'; }
class Absent {}

describe('injectOptional', () => {
  it('außerhalb eines Injektors (Spec baut mit new): null statt NG0203', () => {
    expect(injectOptional(Present)).toBeNull();
  });

  it('im Injektor: der Dienst, bzw. null, wenn ihn niemand bereitstellt', () => {
    const injector = Injector.create({ providers: [{ provide: Present, useValue: new Present() }] });
    runInInjectionContext(injector, () => {
      expect(injectOptional(Present)?.tag).toBe('da');
      expect(injectOptional(Absent)).toBeNull();
    });
  });
});

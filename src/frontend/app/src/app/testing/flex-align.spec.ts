import { flexAlign } from './flex-align';

describe('flexAlign', () => {
  it('behandelt flow-start/flow-end wie flex-start/flex-end', () => {
    expect(flexAlign('flow-start')).toBe('flex-start');
    expect(flexAlign('flow-end')).toBe('flex-end');
    expect(flexAlign('flex-start')).toBe('flex-start');
    expect(flexAlign(' flex-end ')).toBe('flex-end');
  });

  it('lässt andere Werte unverändert', () => {
    expect(flexAlign('center')).toBe('center');
    expect(flexAlign('start')).toBe('start');
    expect(flexAlign('space-between')).toBe('space-between');
  });
});

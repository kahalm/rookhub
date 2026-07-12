import { formatDuration } from './duration.util';

describe('formatDuration', () => {
  it('shows minutes below 120 min', () => {
    expect(formatDuration(60)).toEqual({ value: '1', unitKey: 'trainingGoals.min' });
    expect(formatDuration(90 * 60)).toEqual({ value: '90', unitKey: 'trainingGoals.min' });
  });

  it('shows hours from 120 min up to 48 h', () => {
    const r = formatDuration(3 * 3600);
    expect(r.unitKey).toBe('trainingGoals.hours');
    expect(r.value).toBe('3');
  });

  it('shows days from 48 h', () => {
    const r = formatDuration(3 * 86400);
    expect(r.unitKey).toBe('trainingGoals.days');
    expect(r.value).toBe('3');
  });

  it('uses the locale decimal separator', () => {
    expect(formatDuration(Math.round(2.5 * 3600), 'en').value).toBe('2.5');
    expect(formatDuration(Math.round(2.5 * 3600), 'de').value).toBe('2,5');
  });

  it('tolerates null/undefined lang (ngx-translate 18 currentLang() is Signal<string|null>)', () => {
    expect(() => formatDuration(3 * 3600, null)).not.toThrow();
    expect(() => formatDuration(3 * 3600, undefined)).not.toThrow();
    expect(formatDuration(3 * 3600, null).unitKey).toBe('trainingGoals.hours');
  });

  it('clamps negative input to 0', () => {
    expect(formatDuration(-100)).toEqual({ value: '0', unitKey: 'trainingGoals.min' });
  });
});

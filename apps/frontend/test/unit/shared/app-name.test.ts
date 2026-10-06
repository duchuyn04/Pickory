import { describe, expect, it } from 'vitest';
import { APP_NAME } from '../../../src/shared/app-name';

describe('app-name', () => {
  it('exports the app name', () => {
    expect(APP_NAME).toBe('Pickory');
  });
});

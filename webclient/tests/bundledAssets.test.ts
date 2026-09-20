import { describe, it, expect } from 'vitest';
import { readFileSync } from 'fs';
import { join } from 'path';

const root = join(__dirname, '..');

describe('bundled terminal font is referenced consistently', () => {
  it('bundled FiraCode.css declares the legacy family the app references', () => {
    const css = readFileSync(join(root, 'fonts/FiraCode.css'), 'utf8');
    expect(css).toMatch(/'Fira Custom'/);
  });

  it('terminal defaults to the bundled legacy family', () => {
    const source = readFileSync(join(root, 'src/webclient/main.ts'), 'utf8');
    expect(source).toContain('"Fira Custom", Menlo, monospace');
  });
});

describe('chafa.wasm factory is called without locateFile', () => {
  it('imageLoader passes no options: chafa-wasm@0.3.3 ignores the locateFile return value and fetches unhashed <bundleDir>/chafa.wasm', () => {
    const src = readFileSync(join(root, 'src/utils/imageLoader.ts'), 'utf8');
    // No locateFile property may be passed to the factory (mentioning it in
    // a comment is fine); only its truthiness is ever read by chafa-wasm.
    expect(src).not.toMatch(/locateFile\s*:/);
    expect(src).not.toMatch(/['"]\/chafa\.wasm['"]/);
  });
});

import { defineConfig } from 'vite';
import fs from 'node:fs';
import path from 'node:path';

function webclientVersion(): string {
    const pkgPath = path.resolve(import.meta.dirname, 'package.json');
    const pkg = JSON.parse(fs.readFileSync(pkgPath, 'utf8')) as { version?: string };
    if (typeof pkg.version !== 'string' || !pkg.version) {
        throw new Error(`webclient ${pkgPath} has no version string`);
    }
    return pkg.version;
}

export default defineConfig({
  // Both pages are mounted below the AtheriZ static root. Shared absolute
  // assets keep /webclient/ and /atheriz_draw/ compatible with one build.
  // Assets are served via FastAPI's /static mount, so base must be /static/.
  base: '/static/',
  define: {
    __WEBCLIENT_VERSION__: JSON.stringify(webclientVersion()),
  },
  resolve: {
    alias: {
      '@xterm/headless': path.resolve(import.meta.dirname, 'node_modules/@xterm/headless/lib-headless/xterm-headless.mjs'),
      'node:module': path.resolve(import.meta.dirname, 'src/shims/node-module.ts'),
    },
  },
  optimizeDeps: {
    exclude: ['chafa-wasm'],
  },
  build: {
    target: 'esnext',
    rollupOptions: {
      input: {
        draw: path.resolve(import.meta.dirname, 'index.html'),
        webclient: path.resolve(import.meta.dirname, 'webclient/index.html'),
      },
    },
  },
});

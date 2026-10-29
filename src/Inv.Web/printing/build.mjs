import { build } from 'esbuild';
import { fileURLToPath } from 'node:url';
await build({ entryPoints: [fileURLToPath(new URL('./session.mjs', import.meta.url))],
    outfile: fileURLToPath(new URL('../wwwroot/letratag.js', import.meta.url)),
    bundle: true, format: 'esm', platform: 'browser', target: 'es2022', minify: true, legalComments: 'inline' });

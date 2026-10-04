// @ts-check
import { readdir, readFile, rm } from 'node:fs/promises';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { defineConfig } from 'astro/config';

/**
 * Astro copies every source screenshot into _astro/ next to the AVIF/WebP
 * variants it generates. The originals are 1–6 MB PNGs that no page links to,
 * so drop any image the built HTML and CSS never reference.
 * @returns {import('astro').AstroIntegration}
 */
function pruneUnreferencedImages() {
  return {
    name: 'prune-unreferenced-images',
    hooks: {
      'astro:build:done': async ({ dir, logger }) => {
        const root = fileURLToPath(dir);
        const files = await readdir(root, { recursive: true });
        const text = (
          await Promise.all(files.filter((f) => /\.(html|css)$/.test(f)).map((f) => readFile(join(root, f), 'utf8')))
        ).join('\n');
        const unused = files.filter(
          (f) => f.startsWith('_astro') && /\.(png|jpe?g)$/.test(f) && !text.includes(f.split(/[\\/]/).pop() ?? f),
        );
        await Promise.all(unused.map((f) => rm(join(root, f))));
        logger.info(`Removed ${unused.length} unreferenced source images.`);
      },
    },
  };
}

// The Docs workflow sets SITE_URL / SITE_BASE from actions/configure-pages, so
// the same build works on the custom domain (base "/") and on the
// philliphoff.github.io/EncDotNet.S100 project URL before the domain is live.
export default defineConfig({
  site: process.env.SITE_URL || 'https://soundcharts.app',
  base: process.env.SITE_BASE || '/',
  integrations: [pruneUnreferencedImages()],
  // Stand-in screenshots are read from the repo's readme/ and docs/images/.
  vite: { server: { fs: { allow: ['..'] } } },
});

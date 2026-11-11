// Resolves a shot ID to an image: the real capture in src/assets/shots/<ID>.png
// when it exists, otherwise a stand-in from the repo's README screenshots.
// Captures may also have a Light-chrome twin, <ID>.light.png, for visitors whose
// system theme is light; the plain capture (Dark chrome) is the dark one.
import type { ImageMetadata } from 'astro';

type Module = { default: ImageMetadata };

// Lazy globs, so only the images the page uses end up in the build.
const captures = import.meta.glob<Module>('../assets/shots/*.{png,jpg,jpeg,webp}');
const standins = import.meta.glob<Module>(['../../../readme/*.png', '../../../docs/images/*.png']);

const byFileName = async (modules: Record<string, () => Promise<Module>>, name: string) => {
  const load = Object.entries(modules).find(([path]) => path.split('/').pop() === name)?.[1];
  return load ? (await load()).default : undefined;
};

export type ShotSource =
  | { kind: 'capture'; image: ImageMetadata; light?: ImageMetadata }
  | { kind: 'standin'; image: ImageMetadata }
  | { kind: 'todo' };

export async function resolveShot(id: string, standin?: string): Promise<ShotSource> {
  for (const ext of ['png', 'jpg', 'jpeg', 'webp']) {
    const image = await byFileName(captures, `${id}.${ext}`);
    if (image) return { kind: 'capture', image, light: await byFileName(captures, `${id}.light.${ext}`) };
  }
  if (standin) {
    const image = await byFileName(standins, standin);
    if (!image) throw new Error(`Stand-in image '${standin}' for shot ${id} was not found.`);
    return { kind: 'standin', image };
  }
  return { kind: 'todo' };
}

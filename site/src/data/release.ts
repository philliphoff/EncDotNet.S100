// Latest viewer release, read once at build time so visitors never call the
// GitHub API. Any failure falls back to the releases page.
import { links } from './content';

export interface ReleaseInfo {
  tag?: string;
  url: string;
  mac?: string;
  windows?: string;
  linux?: string;
}

// Asset names before and after the SoundCharts rename (#698).
const patterns = {
  mac: /\.dmg$/,
  windows: /^(SoundCharts|Viewer)-win-x64\.zip$/,
  linux: /^(SoundCharts|Viewer)-linux-x64\.tar\.gz$/,
};

const api = 'https://api.github.com/repos/philliphoff/EncDotNet.S100/releases';

function fetchRelease(path: string) {
  const headers: Record<string, string> = { Accept: 'application/vnd.github+json' };
  if (process.env.GITHUB_TOKEN) headers.Authorization = `Bearer ${process.env.GITHUB_TOKEN}`;
  return fetch(`${api}/${path}`, { headers, signal: AbortSignal.timeout(10_000) });
}

export async function getRelease(): Promise<ReleaseInfo> {
  const fallback: ReleaseInfo = { url: links.releases };
  try {
    const res = await fetchRelease('latest');
    if (!res.ok) return fallback;
    const release = (await res.json()) as {
      tag_name: string;
      html_url: string;
      assets: { name: string; browser_download_url: string }[];
    };
    const find = (re: RegExp) => release.assets.find((a) => re.test(a.name))?.browser_download_url;
    return {
      tag: release.tag_name,
      url: release.html_url,
      mac: find(patterns.mac),
      windows: find(patterns.windows),
      linux: find(patterns.linux),
    };
  } catch {
    return fallback;
  }
}

// Whether the rolling `nightly` prerelease exists, so the site only links to it
// once the Nightly workflow has published one. Shared by the hero and footer.
let nightly: Promise<boolean> | undefined;

export function hasNightly(): Promise<boolean> {
  nightly ??= fetchRelease('tags/nightly').then(
    (res) => res.ok,
    () => false,
  );
  return nightly;
}

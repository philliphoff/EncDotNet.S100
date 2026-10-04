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

export async function getRelease(): Promise<ReleaseInfo> {
  const fallback: ReleaseInfo = { url: links.releases };
  try {
    const headers: Record<string, string> = { Accept: 'application/vnd.github+json' };
    if (process.env.GITHUB_TOKEN) headers.Authorization = `Bearer ${process.env.GITHUB_TOKEN}`;
    const res = await fetch('https://api.github.com/repos/philliphoff/EncDotNet.S100/releases/latest', {
      headers,
      signal: AbortSignal.timeout(10_000),
    });
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

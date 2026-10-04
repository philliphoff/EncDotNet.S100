// Page content. Shot IDs match site/shot-list.md; `standin` names an existing
// README screenshot to show until the real capture lands in src/assets/shots/.

export const links = {
  repo: 'https://github.com/philliphoff/EncDotNet.S100',
  releases: 'https://github.com/philliphoff/EncDotNet.S100/releases/latest',
  license: 'https://github.com/philliphoff/EncDotNet.S100/blob/main/LICENSE',
  security: 'https://github.com/philliphoff/EncDotNet.S100/blob/main/SECURITY.md',
  standards: 'https://github.com/philliphoff/EncDotNet.S100#supported-standards',
  nuget: 'https://www.nuget.org/packages?q=EncDotNet.S100',
  discord: 'https://discord.gg/kf6B9EZqqB',
};

// Paths into the DocFX output, which is deployed alongside this site.
export const docs = {
  home: 'docs/',
  gettingStarted: 'docs/getting-started.html',
  cli: 'docs/cli.html',
  mcp: 'docs/mcp-server.html',
  api: 'api/',
};

export interface Product {
  shot: string;
  spec: string;
  caption: string;
  standin?: string;
  placeholder?: string;
  /** Hidden on narrow screens, which show eight tiles. */
  extra?: boolean;
  /** Data credit shown under the tile, linking to the matching notice in the footer. */
  credit?: { text: string; href: string };
}

export const products: Product[] = [
  // Quebec City harbour, Canadian Hydrographic Service sample data (CHS notice in the footer).
  {
    shot: 'P01', spec: 'S-101', caption: 'Electronic charts',
    credit: { text: 'Quebec City · contains CHS data, not for navigation', href: '#chs-notice' },
  },
  { shot: 'P02', spec: 'S-57', caption: "Today's charts, translated" },
  { shot: 'P03', spec: 'S-102', caption: 'Bathymetry' },
  { shot: 'P04', spec: 'S-104', caption: 'Water levels' },
  { shot: 'P05', spec: 'S-111', caption: 'Surface currents' },
  { shot: 'P06', spec: 'S-124', caption: 'Navigational warnings' },
  { shot: 'P07', spec: 'S-125', caption: 'Aids to navigation' },
  { shot: 'P08', spec: 'S-411', caption: 'Sea ice' },
  { shot: 'P09', spec: 'S-421', caption: 'Route plans', extra: true },
  { shot: 'P10', spec: 'S-129', caption: 'Under-keel clearance', extra: true },
  { shot: 'P11', spec: 'S-401', caption: 'Inland waterways', placeholder: 'Inland ENC', extra: true },
  { shot: 'P12', spec: 'S-131', caption: 'Harbour infrastructure', extra: true },
];

export const standards: [spec: string, subject: string][] = [
  ['S-101', 'Electronic Navigational Charts'],
  ['S-102', 'Bathymetric Surfaces'],
  ['S-104', 'Water Level Information'],
  ['S-111', 'Surface Currents'],
  ['S-122', 'Marine Protected Areas'],
  ['S-124', 'Navigational Warnings'],
  ['S-125', 'Marine Aids to Navigation'],
  ['S-127', 'Marine Resources & Services'],
  ['S-128', 'Catalogue of Nautical Products'],
  ['S-129', 'Under Keel Clearance'],
  ['S-131', 'Harbour Infrastructure'],
  ['S-201', 'IALA AtoN Information'],
  ['S-411', 'Sea Ice Information'],
  ['S-421', 'Route Plans'],
  ['S-401', 'Inland ENC (IEHG)'],
  ['S-57', 'Legacy ENC, via S-101'],
];

/** Prefixes a site-relative path (e.g. a DocFX page) with the deployment base. */
export const withBase = (path: string) => import.meta.env.BASE_URL.replace(/\/?$/, '/') + path;

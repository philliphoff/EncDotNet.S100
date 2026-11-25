namespace EncDotNet.S100.Cli.Reference;

/// <summary>
/// The style sheet and search script of an exported chart reference (R14,
/// #1723). Both are written next to the pages, so the site needs no network:
/// system fonts only, no external scripts.
/// </summary>
internal static class ReferenceAssets
{
    /// <summary>
    /// The style sheet. The page follows the export's palette: light for
    /// Day, dark for Dusk and Night, as a bridge at night would want it. The
    /// exporter appends each palette's glyph tile colour.
    /// </summary>
    public const string StyleSheet = """
        :root { --bg: #ffffff; --fg: #1b1f24; --muted: #5b6470; --line: #d9dee4; --accent: #0b5cad; --badge: #eef2f6; --panel: #f6f8fa; }
        html.dusk, html.night { --bg: #101418; --fg: #e6e9ec; --muted: #9aa4ae; --line: #2c343c; --accent: #7fb8ff; --badge: #1d252d; --panel: #161c22; }
        html.night { --bg: #050607; --fg: #b9bec4; --muted: #7d858d; --line: #1e2328; --accent: #6b9fd8; --badge: #12171b; --panel: #0b0e10; }
        * { box-sizing: border-box; }
        body { margin: 0; padding: 0 16px 48px; background: var(--bg); color: var(--fg); font: 15px/1.5 system-ui, -apple-system, "Segoe UI", Roboto, sans-serif; }
        a { color: var(--accent); }
        a.term { color: inherit; text-decoration: underline dotted; text-underline-offset: 3px; }
        code { font: 13px ui-monospace, SFMono-Regular, Menlo, Consolas, monospace; }
        h1 { font-size: 1.6em; margin: 0; line-height: 1.2; }
        h2 { font-size: 1.1em; margin: 0 0 8px; display: inline; }
        h3 { font-size: 1em; margin: 20px 0 6px; }
        .top, main { max-width: 1100px; margin: 0 auto; }
        .top { padding-top: 24px; }
        .note, .code, .kind, .count, .caption { color: var(--muted); }
        .kind { margin: 0; font-size: 0.85em; text-transform: uppercase; letter-spacing: 0.06em; }
        .code { margin: 2px 0; }
        .badge { display: inline-block; padding: 0 8px; border-radius: 10px; background: var(--badge); border: 1px solid var(--line); font-size: 0.8em; color: var(--fg); }
        #search { width: 100%; max-width: 560px; padding: 8px 12px; font: inherit; color: var(--fg); background: var(--panel); border: 1px solid var(--line); border-radius: 6px; }
        .kinds { display: flex; flex-wrap: wrap; gap: 4px 16px; margin: 8px 0 16px; }
        .group { margin-top: 24px; }
        .group h3 { color: var(--muted); }
        .group h2 { display: block; border-bottom: 1px solid var(--line); padding-bottom: 4px; }
        ul.entries { list-style: none; padding: 0; margin: 8px 0; display: grid; grid-template-columns: repeat(auto-fill, minmax(260px, 1fr)); gap: 2px 16px; }
        ul.entries a { display: flex; align-items: center; gap: 8px; padding: 2px 0; text-decoration: none; color: var(--fg); min-height: 32px; }
        ul.entries a:hover .name { text-decoration: underline; }
        ul.entries .label { display: flex; flex-direction: column; min-width: 0; line-height: 1.3; }
        ul.entries .code { font-size: 0.8em; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
        .tile { display: inline-flex; flex: none; align-items: center; justify-content: center; border-radius: 4px; border: 1px solid var(--line); vertical-align: middle; }
        .tile.small { width: 32px; height: 32px; }
        .tile.big { width: 96px; height: 96px; }
        .tile img { width: 100%; height: 100%; }
        .tile.empty { background: transparent !important; border-style: dashed; }
        .crumbs { max-width: 1100px; margin: 0 auto; padding: 16px 0 0; font-size: 0.9em; }
        .entry .head { display: flex; gap: 16px; align-items: center; margin: 16px 0; }
        .definition { max-width: 760px; }
        dl.facts { display: grid; grid-template-columns: max-content 1fr; gap: 4px 16px; margin: 16px 0; }
        dl.facts dt { color: var(--muted); }
        dl.facts dd { margin: 0; }
        .section { margin: 20px 0; padding-top: 12px; border-top: 1px solid var(--line); }
        details.section summary { cursor: pointer; }
        details.section[open] summary { margin-bottom: 8px; }
        ul.items { list-style: none; padding: 0; margin: 8px 0; }
        ul.items > li { padding: 6px 0; border-bottom: 1px solid var(--line); }
        ul.items > li > .tile { margin-right: 8px; }
        ul.cases { margin: 4px 0 0 40px; padding-left: 16px; color: var(--fg); }
        ul.items .note { margin: 2px 0 0 40px; }
        ul.links { columns: 3 220px; }
        .palettes { display: flex; gap: 16px; }
        .palettes figure { margin: 0; text-align: center; }
        .palettes figcaption { color: var(--muted); font-size: 0.85em; }
        .scroll { overflow-x: auto; }
        table { border-collapse: collapse; margin: 8px 0 16px; }
        th, td { border: 1px solid var(--line); padding: 4px 8px; text-align: left; vertical-align: top; }
        thead th { background: var(--panel); }
        tbody th { font-weight: normal; background: var(--panel); }
        table.values td:first-child { white-space: nowrap; }
        table.values tr:target { outline: 2px solid var(--accent); }
        .parts { display: inline-flex; flex-wrap: wrap; gap: 4px; }
        .part { display: inline-flex; align-items: center; gap: 4px; text-decoration: none; color: var(--fg); }
        .part .id { font-size: 0.75em; color: var(--muted); }
        .part.text { font-size: 0.85em; color: var(--muted); }
        .none, .more { color: var(--muted); font-size: 0.85em; }
        .missing { color: var(--muted); }
        ul.caption { margin: 16px 0 4px; padding-left: 20px; color: var(--muted); }
        .hidden { display: none !important; }
        """;

    /// <summary>
    /// The index page's search: filters the entries by name, code and S-57
    /// acronym as the user types, hides empty groups, and reads a query
    /// from the address (<c>index.html#q=beacon</c>). Without scripts, the
    /// index still lists every entry.
    /// </summary>
    public const string SearchScript = """
        (function () {
          "use strict";
          var input = document.getElementById("search");
          var count = document.getElementById("count");
          if (!input) return;
          var items = Array.prototype.slice.call(document.querySelectorAll("ul.entries > li"));
          var groups = Array.prototype.slice.call(document.querySelectorAll("section.group"));
          function apply() {
            var words = input.value.toLowerCase().split(/\s+/).filter(function (w) { return w.length > 0; });
            var shown = 0;
            items.forEach(function (li) {
              var text = li.getAttribute("data-s") || "";
              var match = words.every(function (w) { return text.indexOf(w) >= 0; });
              li.classList.toggle("hidden", !match);
              if (match) shown++;
            });
            groups.forEach(function (g) {
              g.classList.toggle("hidden", g.querySelector("ul.entries > li:not(.hidden)") === null);
              Array.prototype.forEach.call(g.querySelectorAll("h3"), function (h) {
                var list = h.nextElementSibling;
                h.classList.toggle("hidden", !!list && list.querySelector("li:not(.hidden)") === null);
              });
            });
            count.textContent = words.length === 0 ? "" : shown + (shown === 1 ? " entry" : " entries");
          }
          var hash = /^#q=(.*)$/.exec(location.hash);
          if (hash) input.value = decodeURIComponent(hash[1]);
          input.addEventListener("input", apply);
          apply();
        })();
        """;
}

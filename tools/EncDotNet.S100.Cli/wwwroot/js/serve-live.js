// The live link of the tiles serve web page (issue #1583, handoff D28, D30).
// While the circuit is down the page can't render, so this marks
// <html data-link="reconnecting|disconnected|stopped"> and CSS swaps the
// state pill's label and shows a banner (Shell/StatePill, Shell/LinkBanner).
// It replaces Blazor's reconnect dialog: the page has a
// #components-reconnect-modal element, so Blazor reports its reconnection
// states as components-reconnect-state-changed events instead of drawing one.
//
// Amber (reconnecting) when /healthz answers: tiles still load over plain
// HTTP. Red (disconnected) when it doesn't, with a countdown to the next try.
// Red (stopped) after the server said it was stopping. A server that answers
// but has forgotten this page (restarted) gets the page loaded again.
window.s100Live = (() => {
  const RETRY_SECONDS = 8;
  const root = document.documentElement;

  let started = false;
  let down = false;
  let stopped = false;
  let gone = false;
  let gaveUp = false;
  let checking = false;
  let timer;
  let due = 0;

  function show(state) {
    if (state === "live") delete root.dataset.link;
    else root.dataset.link = state;
  }

  async function healthy() {
    try {
      const response = await fetch("healthz", { cache: "no-store" });
      return response.ok;
    } catch {
      return false;
    }
  }

  function countdown() {
    const seconds = Math.max(0, Math.ceil((due - Date.now()) / 1000));
    const text = checking || seconds === 0 ? "Trying again…" : `Trying again in ${seconds} s.`;
    for (const element of document.querySelectorAll("[data-link-countdown]")) element.textContent = text;
  }

  function schedule() {
    clearTimeout(timer);
    due = Date.now() + RETRY_SECONDS * 1000;
    countdown();
    timer = setTimeout(check, RETRY_SECONDS * 1000);
  }

  // Ask the server whether it's there; if it is, rejoin (or reload when it's a new server).
  async function check() {
    if (!down || checking) return;
    checking = true;
    clearTimeout(timer);
    countdown();
    try {
      if (!(await healthy())) {
        gone = true;
        show(stopped ? "stopped" : "disconnected");
        return;
      }
      if (stopped) {
        // It stopped and is back (it answered before it went, while stopping): what it serves may differ, so start afresh.
        if (gone) location.reload();
        return;
      }
      show("reconnecting");
      if (gaveUp && window.Blazor?.reconnect) {
        // Blazor stopped retrying on its own; false means the server no longer knows this page.
        const rejoined = await window.Blazor.reconnect();
        if (rejoined === false) location.reload();
      }
    } catch {
      // Try again on the next tick.
    } finally {
      checking = false;
      if (down) schedule();
    }
  }

  function onReconnectState(e) {
    switch (e.detail?.state) {
      case "show":
      case "retrying":
        if (!down) {
          down = true;
          show(stopped ? "stopped" : "reconnecting");
          check();
        }
        break;
      case "hide":
        down = false;
        gaveUp = false;
        clearTimeout(timer);
        show("live");
        break;
      case "failed":
        gaveUp = true;
        break;
      case "rejected":
        // The server answers but has no circuit for this page: it restarted.
        gaveUp = true;
        healthy().then(ok => { if (ok) location.reload(); });
        break;
    }
  }

  function onClick(e) {
    if (e.target?.closest?.("[data-link-retry]")) check();
  }

  return {
    // Once the page is interactive.
    start() {
      if (started) return;
      started = true;
      // Dispatched on the element, and it doesn't bubble.
      document.getElementById("components-reconnect-modal")?.addEventListener("components-reconnect-state-changed", onReconnectState);
      document.addEventListener("click", onClick);
      setInterval(() => { if (down) countdown(); }, 1000);
    },

    // What's served changed, and the browser tab says what it is (Q1).
    setTitle(title) {
      document.title = title;
    },

    // The server is stopping cleanly (D28's Server stopped).
    // Blazor doesn't try to reconnect after a clean close, so this watches for the server's return itself.
    stopping() {
      stopped = true;
      down = true;
      show("stopped");
      schedule();
    },
  };
})();

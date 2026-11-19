# EncDotNet.S100.WebApp (spike)

A Blazor WebAssembly front end for `s100 tiles serve`. Findings and recommendation:
[docs/design/serve-webapp-blazor-spike.md](../../docs/design/serve-webapp-blazor-spike.md).

It isn't in the solution, the CLI or CI.

## Run it

1. Start a serve instance:

   ```bash
   s100 tiles serve --layer enc.000 --port 8765
   ```

2. Run the app:

   ```bash
   dotnet run --project spikes/EncDotNet.S100.WebApp -c Release
   ```

3. Open the URL it prints, adding `?server=http://127.0.0.1:8765/`.

**Open locally** parses an S-101 `.000` in the browser. Pick a file, or pass `?url=`
with a path relative to the app (put the cell in `wwwroot`).

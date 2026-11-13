# Run s100 in Docker

The `s100` command-line tool is also published as a container image,
`ghcr.io/philliphoff/s100`, for Linux on x64 and arm64. Use it to serve chart
tiles from a server or a small computer, or to give an AI agent the S-100 MCP
tools without installing `s100` itself. The image runs the same `s100` that the
[release archives](cli.md#install) hold. Its entry point is `s100`, so the
arguments after the image name are an `s100` command:

```bash
docker run --rm ghcr.io/philliphoff/s100 --version
```

## Prerequisites

- Docker on Linux, macOS or Windows.
- Datasets in a folder on the computer that runs Docker.

## Choose a tag

| Tag | What it holds |
|---|---|
| `latest` | The latest release. |
| `<version>`, such as `0.26.0` | That release. |
| `<major>.<minor>`, such as `0.26` | The latest patch release of that version. |
| `nightly` | The latest [nightly build](cli.md#nightly-builds). Unsupported. |

## Serve tiles from a folder of charts

Mount the folder at `/data` and run [`tiles serve`](cli.md#tiles-serve):

```bash
docker run --rm -p 8200:8200 -v path/to/charts:/data:ro ghcr.io/philliphoff/s100 tiles serve exchange-set.zip --host 0.0.0.0 --no-token
```

Then open `http://localhost:8200/` for the preview map, or use
`http://localhost:8200/tiles.json` as a raster source in a web map.

- `/data` is the working folder, so paths are relative to the folder you
  mounted. The image only reads it, so `:ro` is fine.
- `--host 0.0.0.0` is needed because Docker forwards the port to the
  container's network address, not to its loopback address.
- The URLs that `tiles serve` prints use the container's own address. Reach
  the server through the address of the computer that runs Docker, and the
  port you published with `-p`.
- Without `--no-token`, `tiles serve` adds a new random token to every URL each
  time it starts. Pass `--token <token>` instead to keep the URLs the same
  across restarts. Use `--no-token` only on a network you trust, or behind a
  proxy that checks access.

`tiles serve` renders every dataset in the folder you give it, searching its
subfolders: S-57 cells (an `ENC_ROOT` with its `CATALOG.031` included), S-100
exchange sets and loose datasets. A folder that holds a `tiles.json` or
numbered `{z}/{x}` subfolders is served as a built tile set instead. To serve
only some of the datasets, name them with `--layer`, or restrict the folder to
some product specifications with `--only`:

```bash
docker run --rm -p 8200:8200 -v path/to/charts:/data:ro ghcr.io/philliphoff/s100 tiles serve /data --only S57,S102 --host 0.0.0.0 --no-token
```

`tiles serve` checks the datasets for changes every 10 seconds, so you can copy
a new edition, or a new cell, into the mounted folder while it runs, or remove
one.

To run on a small computer, render the tiles in advance instead: see
[Serve prebuilt tiles](#serve-prebuilt-tiles).

## Keep tiles and data between runs

The image runs `s100` as a non-root user (user ID 1654) and has these folders:

| Folder | Holds | Writable |
|---|---|---|
| `/data` | Your datasets. The working folder. | No |
| `/cache` | Rendered tiles, with `tiles serve --cache-dir /cache`. | Yes |
| `/state` | What `s100` keeps per user: the tide station catalogue, and an MCP Library with `--data-dir /state/library`. | Yes |

Mount a named volume on `/cache` to keep rendered tiles across restarts:

```bash
docker run --rm -p 8200:8200 -v path/to/charts:/data:ro -v s100-tiles:/cache ghcr.io/philliphoff/s100 tiles serve exchange-set.zip --host 0.0.0.0 --no-token --cache-dir /cache
```

A named volume takes the folder's owner from the image. If you mount a folder
from your computer on `/cache` or `/state` instead, user ID 1654 must be able
to write to it.

## Run with Docker Compose

This `compose.yaml` serves an exchange set, keeps its tiles, restarts the
server if it stops, and checks its health:

```yaml
services:
  tiles:
    image: ghcr.io/philliphoff/s100
    command: tiles serve exchange-set.zip --host 0.0.0.0 --no-token --cache-dir /cache
    ports:
      - "8200:8200"
    volumes:
      - ./charts:/data:ro
      - tile-cache:/cache
    healthcheck:
      test: ["CMD", "/app/s100", "health"]
      interval: 30s
      timeout: 10s
      start_period: 2m
      start_interval: 2s
    restart: unless-stopped

volumes:
  tile-cache:
```

```bash
docker compose up -d
```

`docker compose ps` shows the service as `healthy` once it serves tiles. The
first start portrays every dataset before it serves, so `start_period` allows
for a large exchange set.

## Serve prebuilt tiles

`tiles serve` over datasets portrays every dataset when it starts and renders
each tile the first time it's asked for. On a small computer, such as a
Raspberry Pi, that takes minutes and gigabytes of memory. Instead, render the
tiles once with [`tiles export`](cli.md#tiles-export) and serve the finished
archive: the server then starts at once, uses little memory, and answers every
tile by reading it from the file.

The example
[`compose.yaml`](https://github.com/philliphoff/EncDotNet.S100/blob/main/tools/EncDotNet.S100.Cli/docker/examples/prebuilt-tiles/compose.yaml)
has two services. `export` renders every dataset in `./charts` into a PMTiles
archive in the `tiles` volume, and `tiles` serves it:

```yaml
services:
  export:
    image: ghcr.io/philliphoff/s100
    command: tiles export /data -o /cache/charts.pmtiles --skip-empty --yes
    volumes:
      - ./charts:/data:ro
      - tiles:/cache
    profiles: [export]

  tiles:
    image: ghcr.io/philliphoff/s100
    command: tiles serve /cache/charts.pmtiles --host 0.0.0.0 --no-token
    ports:
      - "8200:8200"
    volumes:
      - tiles:/cache:ro
    healthcheck:
      test: ["CMD", "/app/s100", "health"]
      interval: 30s
      timeout: 10s
      start_period: 10s
      start_interval: 1s
    restart: unless-stopped

volumes:
  tiles:
```

Render the tiles, then start the server:

```bash
docker compose run --rm export
```

```bash
docker compose up -d
```

The `export` service is in the `export` profile, so `docker compose up` starts
only the server. Run the export again whenever the charts change. The server
keeps serving the old archive while the new one is written, and serves the new
one as soon as it's finished, without a restart.

`tiles export` prints the number of tiles before it renders any. A large
folder of charts makes a large tile set: each zoom level holds four times the
tiles of the one before. Add `--bbox` or `--max-zoom` to the export command to
render only the area and the detail you need. See
[Zoom levels](cli.md#zoom-levels).

A tile set holds one palette. To serve day and night tiles, export each to its
own archive with `--palette`, and run a `tiles serve` for each.

As a rough guide, here are 186 NOAA ENC cells around Massachusetts, cut to
Boston Harbor (`--bbox -71.12,42.28,-70.88,42.42`, zoom 10 to 16), with the
container limited to 4 CPUs and 4 GB of memory on an arm64 Mac:

| | Prebuilt | Rendered on demand |
|---|---|---|
| Export | 39 s, once: 1,992 tiles, 23 MB | none |
| Healthy after starting | 1 s | 39 s |
| First request for each of 311 tiles | median 2 ms, slowest 13 ms | median 2 ms, slowest 269 ms |
| Memory while serving | 37 MB | 2.5 GB |

A Raspberry Pi's cores are slower than a Mac's, so expect the export and the
on-demand start to take longer there. Rendering on demand needs memory for
every portrayed dataset; serving prebuilt tiles doesn't, so it suits a
computer with little memory. You can also run the export on another computer
and copy the archive to the one that serves it.

## Check a server's health

`tiles serve` and `feed serve` answer `GET /healthz` with `200 OK` and the text
`ok` while they run. The path never has a token, and the answer says nothing
about what's served.

The image has no shell or `curl`, so a health check runs
[`s100 health`](cli.md#health) in the container. It asks the server on port
8200 and exits with 0 when the server answers. Pass `--port 8100` for
`feed serve`, and `--port 8300` for `mcp serve --transport http`. Kubernetes and load balancers can ask `/healthz` directly.

## Give an agent the MCP tools

[`mcp serve`](cli.md#mcp-serve) talks MCP over standard input and output. An
MCP client that starts servers as commands can start it in a container. Pass
`-i` so the container reads standard input, and not `-t`:

```json
{
  "mcpServers": {
    "s100": {
      "command": "docker",
      "args": [
        "run", "-i", "--rm",
        "-v", "/path/to/charts:/data:ro",
        "-v", "s100-state:/state",
        "ghcr.io/philliphoff/s100",
        "mcp", "serve", ".", "--data-dir", "/state/library"
      ]
    }
  }
}
```

`.` serves every dataset in the mounted folder. The agent can read only the
folders you mount. `--data-dir /state/library` keeps the Library's collections
and downloads in the `s100-state` volume; without it they're removed when the
server stops.

### Serve MCP over HTTP

To run the MCP server as a long-running service that agents connect to, serve
it over HTTP with `--transport http`. It listens on port 8300 at `/mcp`:

```yaml
services:
  mcp:
    image: ghcr.io/philliphoff/s100
    command: mcp serve . --transport http --host 0.0.0.0 --token ${S100_MCP_TOKEN:?set S100_MCP_TOKEN} --data-dir /state/library
    ports:
      - "8300:8300"
    volumes:
      - ./charts:/data:ro
      - s100-state:/state
    healthcheck:
      test: ["CMD", "/app/s100", "health", "--port", "8300"]
      interval: 30s
      timeout: 10s
      start_period: 2m
      start_interval: 2s
    restart: unless-stopped

volumes:
  s100-state:
```

```bash
S100_MCP_TOKEN=$(openssl rand -hex 16) docker compose up -d
```

Agents connect to `http://<host>:8300/mcp` and send the token in an
`Authorization: Bearer <token>` header. For example, with Claude Code:

```bash
claude mcp add --transport http s100 http://localhost:8300/mcp --header "Authorization: Bearer $S100_MCP_TOKEN"
```

Without `--token`, `mcp serve` generates a new token each time it starts and
prints it in the container's log (`docker compose logs mcp`).

Over HTTP, the server leaves out the tools that open datasets by path and
change the Library, so a client can't make it read other files in the
container or fetch other URLs. The datasets in `/data` and any `--collection`
sources are all it serves. Pass `--allow-changes` to serve those tools too.
See [MCP over HTTP](cli.md#mcp-over-http).

Use HTTP only on a network you trust, or behind a proxy that adds HTTPS: the
server itself speaks plain HTTP, so the token travels unencrypted.

## Predict tides

The tide commands read the station catalogue from `/state`. Download it into a
volume once, then mount the volume wherever you predict tides:

```bash
docker run --rm -v s100-state:/state ghcr.io/philliphoff/s100 tides update
docker run --rm -v s100-state:/state ghcr.io/philliphoff/s100 tides predict 9447130
```

## Update the image

The image doesn't check for new releases. Pull the tag again to update:

```bash
docker pull ghcr.io/philliphoff/s100
```

## Build the image from source

From a clone of the repository, this script publishes `s100` for your Docker
engine's architecture and builds the image as `s100:dev`. It needs the .NET SDK
and Docker Buildx:

```bash
tools/EncDotNet.S100.Cli/docker/build-local.sh
```

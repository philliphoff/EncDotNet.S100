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

`tiles serve` renders an S-100 exchange set (a folder holding `CATALOG.XML`, or
a `.zip`), datasets given with `--layer`, or serves a built tile set. It reads
any other folder as a built tile set, so give S-57 cells and loose datasets
with `--layer`:

```bash
docker run --rm -p 8200:8200 -v path/to/charts:/data:ro ghcr.io/philliphoff/s100 tiles serve --layer ENC_ROOT/US5MA1BO/US5MA1BO.000 --layer bathy.h5 --host 0.0.0.0 --no-token
```

`tiles serve` checks the datasets for changes every 10 seconds, so you can copy
a new edition into the mounted folder while it runs.

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

## Check a server's health

`tiles serve` and `feed serve` answer `GET /healthz` with `200 OK` and the text
`ok` while they run. The path never has a token, and the answer says nothing
about what's served.

The image has no shell or `curl`, so a health check runs
[`s100 health`](cli.md#health) in the container. It asks the server on port
8200 and exits with 0 when the server answers. Pass `--port 8100` for
`feed serve`. Kubernetes and load balancers can ask `/healthz` directly.

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

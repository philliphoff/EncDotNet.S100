#!/usr/bin/env bash
# Builds the s100 container image from the working tree, for this machine's
# architecture, and loads it into the local Docker engine as s100:dev.
#
#   tools/EncDotNet.S100.Cli/docker/build-local.sh [tag]
#
# CI builds the published image from the release archives instead
# (.github/workflows/container.yml).
set -euo pipefail

tag="${1:-s100:dev}"
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo="$(cd "$here/../../.." && pwd)"

case "$(docker version --format '{{.Server.Arch}}')" in
  amd64 | x86_64) arch=amd64 rid=linux-x64 ;;
  arm64 | aarch64) arch=arm64 rid=linux-arm64 ;;
  *) echo "Unsupported Docker architecture: $(docker version --format '{{.Server.Arch}}')" >&2; exit 1 ;;
esac

context="$(mktemp -d)"
trap 'rm -rf "$context"' EXIT

dotnet publish "$repo/tools/EncDotNet.S100.Cli/EncDotNet.S100.Cli.csproj" \
  --configuration Release \
  --runtime "$rid" \
  --self-contained true \
  --output "$context/linux-$arch"

docker buildx build \
  --platform "linux/$arch" \
  --file "$here/Dockerfile" \
  --build-arg REVISION="$(git -C "$repo" rev-parse HEAD)" \
  --tag "$tag" \
  --load \
  "$context"

echo "Built $tag for linux/$arch."

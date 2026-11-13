# syntax=docker/dockerfile:1

# The s100 CLI as a container image (issue #1464).
#
# The image packages the self-contained linux-x64 / linux-arm64 CLI that the
# Publish workflow already builds, so it ships the same binaries as the release
# archives. The build context holds one extracted archive per architecture:
#
#   linux-amd64/s100 ...
#   linux-arm64/s100 ...
#
# CI fills it from the s100-cli-linux-* artifacts (.github/workflows/container.yml);
# build-local.sh fills it from a local dotnet publish.

# Folders the image runs with, made on the build machine: the final stage has
# no shell, and nothing here runs under emulation for the other architecture.
FROM --platform=$BUILDPLATFORM busybox:1.37 AS folders
RUN mkdir -p /out/data /out/cache /out/state \
    && chown 1654:1654 /out/cache /out/state

# Ubuntu chiseled: no shell or package manager. The "extra" flavour adds ICU and
# tzdata, which .NET globalization and local-time output need.
FROM mcr.microsoft.com/dotnet/runtime-deps:10.0-noble-chiseled-extra

ARG TARGETARCH
ARG VERSION=0.0.0-dev
ARG REVISION=unknown

LABEL org.opencontainers.image.title="s100" \
      org.opencontainers.image.description="The s100 command-line tool for IHO S-100 datasets: render, serve tiles, validate, check routes and serve MCP tools." \
      org.opencontainers.image.source="https://github.com/philliphoff/EncDotNet.S100" \
      org.opencontainers.image.documentation="https://soundcharts.app/docs/docker.html" \
      org.opencontainers.image.licenses="MIT" \
      org.opencontainers.image.version="${VERSION}" \
      org.opencontainers.image.revision="${REVISION}"

COPY --from=folders /out/ /
COPY linux-${TARGETARCH}/ /app/

# /state holds what s100 keeps per user (the tide station catalogue): .NET's
# local application data is $XDG_DATA_HOME on Linux. Update checks are off;
# a new image is how a container updates.
ENV XDG_DATA_HOME=/state \
    S100_NO_UPDATE_CHECK=1

# The chiseled image's non-root "app" user.
USER 1654
WORKDIR /data

# tiles serve's default port. feed serve uses 8100.
EXPOSE 8200

ENTRYPOINT ["/app/s100"]
CMD ["--help"]

# syntax=docker/dockerfile:1
#
# Build ReControl Desktop Linux installers (.deb / .rpm / .tar.gz) from any host
# that has Docker — including Windows with Docker Desktop. No local .NET / fpm /
# rpm toolchain required.
#
# Usage (from recontrol_desktop/):
#   docker build -f installer/linux/Dockerfile --output type=local,dest=dist .
#
# Artifacts land in recontrol_desktop/dist/. Requires ReControl.Desktop/.env.prod
# to exist (build-linux.sh copies it to .env).

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS builder

# fpm (Ruby) builds the .deb/.rpm; rpm provides rpmbuild; cpio is the rpm payload
# format; curl/tar/xz fetch and unpack the FFmpeg shared libs.
RUN apt-get update && apt-get install -y --no-install-recommends \
        ruby ruby-dev build-essential rpm cpio binutils \
        curl tar xz-utils ca-certificates \
    && gem install --no-document fpm \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /work
COPY . .

# Reuse the existing one-command Linux build. It ensures .env from .env.prod,
# fetches FFmpeg .so libs, self-contained-publishes linux-x64, stages the tree,
# and emits dist/*.deb, dist/*.rpm, dist/*.tar.gz.
RUN bash scripts/build-linux.sh

# Export-only stage: `--output type=local,dest=dist` copies just these files to
# the host, so nothing but the artifacts leaves the container.
FROM scratch AS artifacts
COPY --from=builder /work/dist/ /

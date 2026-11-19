# hakutaku-ingest, the C++ telemetry ingestion service.
# Stage 1 builds it and runs its unit tests, so a broken build or a failing
# test stops the image (and a Jenkins deploy) right here. Stage 2 is just the
# binary on a slim Debian, running as a non-root user.

FROM debian:bookworm AS build
RUN apt-get update \
    && apt-get install -y --no-install-recommends g++ cmake make git ca-certificates \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /src
COPY CMakeLists.txt ./
COPY src ./src
COPY tests ./tests
RUN cmake -S . -B build -DCMAKE_BUILD_TYPE=Release \
    && cmake --build build -j"$(nproc)" \
    && ctest --test-dir build --output-on-failure

FROM debian:bookworm-slim
RUN useradd --system --no-create-home --uid 10001 ingest
COPY --from=build /src/build/hakutaku-ingest /usr/local/bin/hakutaku-ingest
USER ingest
EXPOSE 9000
ENTRYPOINT ["hakutaku-ingest"]

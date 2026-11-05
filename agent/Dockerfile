FROM rust:1.75-alpine as builder

WORKDIR /build

RUN apk add --no-cache \
    musl-dev \
    openssl-dev \
    pkg-config

COPY Cargo.* ./
COPY src ./src

RUN cargo build --release

# Runtime image
FROM alpine:3.18

RUN apk add --no-cache \
    ca-certificates \
    openssl \
    libgcc

WORKDIR /app

COPY --from=builder /build/target/release/agent .

ENV RUST_LOG=info

ENTRYPOINT ["./agent"]

# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- Initial release: multi-connection segmented downloads (`fdl <url>`), multi-file/batch mode,
  recursive folder mirroring with extension filtering (`--folder`, `--ext`), multi-source mirror
  striping (`--mirror`), zip auto-extract and bundle-to-zip (`--extract`, `--zip`), chunk-bitmap
  resume (`.fdlmeta`) for interrupted downloads, and configurable concurrency (`-c`, `-p`,
  `--chunk`). Built on .NET 10 with `SocketsHttpHandler` connection pooling, HTTP/2, and
  lock-free positional disk writes (`RandomAccess.WriteAsync`).

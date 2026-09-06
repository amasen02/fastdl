# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Security

- `--folder` no longer lets a crawled page choose where files land. Containment was checked on
  the still-encoded URL, so an `<a href="..%2f..%2fevil.iso">` on any crawled index passed the
  "inside the root" test and then decoded to `../../evil.iso`, writing above the output
  directory (arbitrary file write — e.g. into `~/.ssh` or a Startup folder). Links are now
  checked after decoding, dot segments are refused, and every resolved path is re-verified
  against the output root before a request is made. The same containment check now covers
  server-supplied `Content-Disposition` file names.
- HTTP Basic credentials typed inline (`https://user:pass@host/…`) are scoped to the origin
  they were typed for. They were previously baked into the one shared `HttpClient` as a default
  header and sent to every other host in the run — other positional URLs, every line of an
  `-i` list, and every `--mirror` source — disclosing the password in reversible base64.
  `--user`/`FDL_PASSWORD` remain run-wide and are now documented as such.

### Removed

- The OpenSSF Best Practices badge, which pointed at an unrelated project's registration and so
  advertised a certification this project does not hold.

### Added

- Initial release: multi-connection segmented downloads (`fdl <url>`), multi-file/batch mode,
  recursive folder mirroring with extension filtering (`--folder`, `--ext`), multi-source mirror
  striping (`--mirror`), zip auto-extract and bundle-to-zip (`--extract`, `--zip`), chunk-bitmap
  resume (`.fdlmeta`) for interrupted downloads, and configurable concurrency (`-c`, `-p`,
  `--chunk`). Built on .NET 10 with `SocketsHttpHandler` connection pooling, HTTP/2, and
  lock-free positional disk writes (`RandomAccess.WriteAsync`).

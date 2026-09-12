# FastDL (`fdl`) — super-speed segmented downloader

[![OpenSSF Scorecard](https://api.securityscorecards.dev/projects/github.com/amasen02/fastdl/badge)](https://securityscorecards.dev/viewer/?uri=github.com/amasen02/fastdl)
[![Security Policy](https://img.shields.io/badge/Security-Policy-blue.svg)](.github/SECURITY.md)


[![CI](https://github.com/amasen02/fastdl/actions/workflows/ci.yml/badge.svg)](https://github.com/amasen02/fastdl/actions/workflows/ci.yml)
[![CodeQL](https://github.com/amasen02/fastdl/actions/workflows/codeql.yml/badge.svg)](https://github.com/amasen02/fastdl/actions/workflows/codeql.yml)
[![.NET](https://img.shields.io/badge/.NET-10-blueviolet)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-green)](LICENSE)
[![PRs welcome](https://img.shields.io/badge/PRs-welcome-brightgreen)](CONTRIBUTING.md)
[![Contributor Covenant](https://img.shields.io/badge/Contributor%20Covenant-2.1-blue)](CODE_OF_CONDUCT.md)

A multi-connection download accelerator: single files, multi-file batches, recursive folder
mirrors, and zip download/extract/pack — built on .NET 10 with true parallelism,
`SocketsHttpHandler` connection pooling, HTTP/2, and lock-free positional disk writes.

## Why it can beat IDM (and the honest ceiling)

The wins are real, but they are *workload-specific* — here is exactly where and why:

| Technique | Effect |
|---|---|
| **Chunk-queue segmentation** (not fixed N-way split) | The file is cut into many small chunks pulled from one shared queue. Fast connections grab more chunks automatically, so there is **no slow-segment long tail** holding up the finish — IDM's fixed segments can stall on one bad connection. |
| **Multi-source / mirror striping** | Give it several URLs for the *same* file (`--mirror`) and it stripes chunks across all of them. When any single source throttles per-connection, total throughput is the **sum** of the mirrors. |
| **Connection pooling + HTTP/2** | One tuned `SocketsHttpHandler`, keep-alive reuse, HTTP/2 multiplexing, no per-request handshake cost. |
| **Lock-free assembly** | Preallocated file + `RandomAccess.WriteAsync` at offsets — segments write concurrently with no shared file pointer and no locking. |
| **Per-segment retry + resume** | Every segment retries with backoff; a sidecar chunk-bitmap (`.fdlmeta`) lets an interrupted download resume only the missing chunks. |

**Honest ceiling:** on a *single, non-throttling* source you are capped by your own bandwidth —
there, no accelerator beats another; they all converge to the link speed. FastDL pulls ahead
specifically on **throttled sources**, **multi-source/mirror** downloads, and **many-file**
workloads (folders/batches), which is most real-world downloading.

## Install

FastDL targets **.NET 10**. Build a single-file executable from source:

```bash
git clone https://github.com/amasen02/fastdl.git
cd fastdl

# Framework-dependent single file (uses the installed .NET 10 runtime):
dotnet publish src/FastDL -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o dist
#   Linux:  -r linux-x64        macOS (Apple Silicon):  -r osx-arm64

# …or fully self-contained (no runtime needed on the target machine):
dotnet publish src/FastDL -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true -o dist
```

The binary is named **`fdl`** (`dist/fdl`, or `dist\fdl.exe` on Windows). Put `dist` on your `PATH`
to call `fdl` from anywhere. To run without publishing:

```bash
dotnet run --project src/FastDL -- <url> [options]
```

## Usage

```
fdl <url> [url2 ...] [options]
fdl -i links.txt [options]
fdl --folder <directory-index-url> [options]
```

### Modes

```bash
# Single file, 32 segments
fdl https://proof.ovh.net/files/100Mb.dat -c 32

# Multi-file (concurrent) into a directory
fdl https://host/a.bin https://host/b.bin -o ./out
fdl -i links.txt -o ./out                 # URLs from a file (one per line, # = comment)

# Recursive folder mirror (Apache/nginx autoindex), filter by extension
fdl --folder https://proof.ovh.net/files/ --ext .dat,.iso -o ./dump

# Multi-source: stripe one file across mirrors
fdl https://a/file.iso --mirror https://b/file.iso --mirror https://c/file.iso

# Zip: download then auto-extract
fdl https://host/archive.zip --extract

# Bundle several downloads into one zip
fdl -i links.txt -o ./out --zip bundle.zip
```

### Options

| Flag | Meaning |
|---|---|
| `-o, --out <path>` | Output file (single) or directory (multi/folder). Default: current dir. **Tip:** end with `/` to force "directory". |
| `-i, --input <file>` | Read newline-separated URLs from a file. |
| `-c, --connections <n>` | Parallel segments per file (default 16, max 256). |
| `-p, --parallel <n>` | Files downloaded concurrently (default 4). |
| `--chunk <size>` | Segment size, e.g. `1M`, `4M`, `512K` (default 4M). |
| `--mirror <url>` | Add an alternate source for the **same** file (repeatable). |
| `--folder` | Crawl each URL as a directory index, recursively. |
| `--depth <n>` | Crawl recursion depth (default 5). |
| `--ext <list>` | Only download these extensions when crawling, e.g. `.iso,.zip`. |
| `--extract` | Extract downloaded zips (detected by content, not just `.zip` name). |
| `--zip <name.zip>` | Pack all downloaded files into one archive. |
| `--no-resume` | Ignore existing partial data and restart. |
| `--header "K: V"` | Add a request header (repeatable) — auth tokens, cookies. |
| `--user <user:pass>` | HTTP Basic auth applied to **every** host in the run (also via `FDL_PASSWORD`). |
| `https://user:pass@host/…` | Inline credentials, scoped to that host only — never sent to the other URLs in the run. |
| `--insecure` | Skip TLS certificate validation. |
| `--retries <n>` | Per-segment retry attempts (default 5). |
| `-q / -v` | Quiet / verbose. |

### Resume

If a download is interrupted (Ctrl+C, crash, network drop), just run the same command again.
Progress is tracked in a `<file>.fdlmeta` sidecar; only the missing chunks are re-fetched, and the
sidecar is deleted on success.

## Tests

```bash
dotnet test FastDL.slnx
```

The suite is **deterministic and offline**: the HTTP layer is driven by a fake
`HttpMessageHandler` that serves an in-memory payload with real Range semantics, so the segmented
engine, resume, single-stream fallback, mirror striping and the directory crawler are all verified
without touching the network. Segmented output is asserted **byte-identical (SHA-256)** to the
source, which proves the lock-free parallel assembly is correct.

## Architecture (separation of concerns)

```
src/FastDL/
  Program.cs              CLI orchestration: parse -> build work list -> run -> post-process
  CommandLine.cs          argv parsing + usage
  DownloadOptions.cs      parsed configuration
  HttpClientProvider.cs   one throughput-tuned HttpClient/SocketsHttpHandler
  SegmentedDownloader.cs  core engine: probe, chunk-queue parallel, single-stream fallback, retry
  BatchDownloader.cs      concurrent multi-file orchestration (semaphore-capped)
  IndexCrawler.cs         recursive autoindex/HTML directory crawler
  ZipService.cs           extract (magic-byte) + pack
  ResumeStore.cs          sidecar chunk-bitmap persistence
  ProgressReporter.cs     lock-free progress aggregation + live render
  Format.cs / Models.cs   helpers + records
tests/FastDL.Tests/       xUnit unit + offline integration tests
```

## Contributing

Contributions are welcome — bug fixes, performance work, new download modes, better docs. See
[`CONTRIBUTING.md`](CONTRIBUTING.md) for the workflow and coding bar, and please be mindful of the
[Code of Conduct](CODE_OF_CONDUCT.md). Use the issue templates; green CI (`build` + `test`) is
required on every pull request. Report security issues privately per [`SECURITY.md`](SECURITY.md) —
never as a public issue.

## Open source commitments

This project is, and will remain, free and open source. As maintainer I commit to:

- **A permissive licence, kept stable.** [MIT](LICENSE) — use it commercially, fork it, build on
  it. No relicensing of accepted contributions.
- **No CLA.** Contributions are accepted under the MIT licence; you keep the copyright to your work.
- **An honest history.** Real, walkable commits — no fabricated activity, no rewritten releases.
- **Best-effort, transparent triage.** Issues and pull requests are read and answered; security
  reports are acknowledged within 72 hours.
- **A welcoming community** governed by the [Code of Conduct](CODE_OF_CONDUCT.md).
- **Reproducible builds.** Green CI — build, tests, and CodeQL security analysis — on every change.

## License

MIT — see [`LICENSE`](LICENSE). You are free to use, modify, and distribute this software,
including for commercial purposes, provided the copyright notice is retained.

## Author

**Ama Senevirathne** — [GitHub](https://github.com/amasen02)

---

## 🌟 Fork, Build Upon & Extend This Project

We deliberately built this repository to be **100% open, modular, and easy to fork and extend**:

- 🔓 **Permissive MIT License**: Zero CLA, commercial use permitted, you keep full ownership of your contributions.
- 🛡️ **Hardened Supply Chain**: Built with automated CI testing, OpenSSF Scorecard supply-chain security, and strict quality checks.
- ⚡ **High-Performance Foundation**: Zero unnecessary bloat &mdash; clean architectural boundaries that make hacking on this code a joy.

### 💡 High-Impact Ideas Ready for You to Build:
- **Add dynamic mirror round-trip latency probing and automatic bandwidth striping**
- **Implement WebAssembly / Blazor web download controller UI**
- **Add BitTorrent chunk piece downloader and magnet URI resolver**
- **Integrate aria2 RPC protocol emulation for drop-in browser extension compatibility**

### 🚀 60-Second Quickstart
```bash
git clone https://github.com/amasen02/fastdl.git
cd fastdl
dotnet run --project src/FastDL.Cli -- download https://example.com/largefile.iso
```

### 🤝 Frictionless Contributions
1. **Fork** the repo & clone it locally.
2. Create your feature branch (`git checkout -b feat/my-awesome-idea`).
3. Verify tests pass cleanly.
4. Open a PR &mdash; we review and merge PRs within 24–48 hours!


---

## 📈 Stargazers Over Time

[![Star History Chart](https://api.star-history.com/svg?repos=amasen02/fastdl&type=Date)](https://star-history.com/#amasen02/fastdl&Date)

⭐ **Found FastDL useful? Please star the repository to support its development!**

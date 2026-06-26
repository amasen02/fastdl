# Contributing to FastDL

Pull requests are welcome — bug fixes, performance work, new download modes, or better docs —
provided they keep the tone and quality of the codebase.

## Ground rules

1. **One concern per pull request.** No drive-by refactors mixed with feature work.
2. **Branch from `master`**, keep the branch short, and squash-merge back.
3. **Conventional commits** (`feat:`, `fix:`, `perf:`, `refactor:`, `test:`, `docs:`, `chore:`).
4. **Green CI is non-negotiable.** `dotnet build` and `dotnet test` must pass before review.
5. **The PR template must be filled.** Empty checkboxes block review.

## Coding standards

- **C# latest, nullable enabled.** No `#nullable disable`; resolve warnings, don't suppress them.
- **Intention-revealing names.** Full descriptive identifiers; `c`, `tmp`, `mgr` are rejected.
- **Comments explain *why*, never *what*.** No filler comments.
- **Async correctness.** Every `async` method takes a `CancellationToken` and propagates it;
  library-level awaits use `ConfigureAwait(false)` (the existing convention here).
- **SOLID / KISS / DRY / YAGNI.** One responsibility per type; the simplest correct solution wins.

## Build, test, run

```bash
dotnet build  FastDL.slnx -c Release
dotnet test   FastDL.slnx                      # unit + offline integration tests
dotnet run --project src/FastDL -- --help      # exercise the CLI
```

## Tests

A pull request that ships behaviour without a test is sent back unless it is purely documentation.

Tests must be **deterministic and offline**: the HTTP layer is exercised through a fake
`HttpMessageHandler` (see `tests/FastDL.Tests/Support/TestHttp.cs`) that serves an in-memory
payload with real Range semantics. Do not add tests that hit the network.

## Reporting bugs and proposing features

Use the issue templates. For security vulnerabilities, **do not open a public issue** — follow
[`SECURITY.md`](SECURITY.md).

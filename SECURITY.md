# Security policy

FastDL is a command-line download client. It makes outbound HTTP(S) requests to URLs **you**
supply and writes files to paths **you** choose. Its security surface is small and local; the
notes below document the few areas worth understanding.

## Security-relevant behaviour

| Area | Behaviour |
|---|---|
| TLS | Certificates are validated by default. `--insecure` disables validation for a run — use it only against hosts you trust; it exposes the transfer to man-in-the-middle tampering. |
| Credentials | HTTP Basic auth is accepted via `--user user:pass`, the `FDL_PASSWORD` environment variable, or inline `https://user:pass@host/…`. Inline userinfo is moved into an `Authorization` header and **stripped from the URL** so it is not written into logs or resume metadata. |
| Resume sidecar | The `.fdlmeta` file stores only a chunk-completion bitmap and resource identity (size, ETag, Last-Modified) — never credentials or response bodies. |
| Path handling | Crawled and Content-Disposition file names are sanitised (invalid characters replaced) and folder crawls are confined to the requested root. |
| Telemetry | None. FastDL makes no network calls other than the downloads you request. |
| Decompression | Transfer compression is disabled (`Accept-Encoding: identity`) so byte-range math stays correct; no archive is auto-expanded unless you pass `--extract`. |

## Reporting a vulnerability

Email `amabandarasp@gmail.com` with the subject prefix `[SECURITY]`, or open a private
[GitHub security advisory](https://github.com/amasen02/fastdl/security/advisories/new).
**Do not open a public issue.** Expect acknowledgement within 72 hours.

## Coordinated disclosure window

90 days from acknowledgement, unless mutually extended.

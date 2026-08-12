# CLAUDE.md

Guidance for AI coding agents working in this repository. See [`ARCHITECTURE.md`](ARCHITECTURE.md) for how the system fits together first.

## What this is

File Access Governance Platform, Phase 1 MVP: scans one Windows file share, ingests security descriptors through Kafka into SQL Server, and answers "who has access to this folder" via a REST API. `src/Shared`, `src/ScanAgent`, `src/IngestionConsumer`, `src/QueryApi` — see `ARCHITECTURE.md` for the full component breakdown.

## Build and test

No .NET SDK is assumed to be installed on the host. Use the official SDK container for everything:

```bash
docker run --rm -v "$(pwd):/work" -w /work mcr.microsoft.com/dotnet/sdk:8.0 dotnet build FileAccessGovernance.sln
docker run --rm -v "$(pwd):/work" -w /work mcr.microsoft.com/dotnet/sdk:8.0 dotnet test FileAccessGovernance.sln
```

If a real .NET 8 SDK **is** available locally, plain `dotnet build`/`dotnet test` work identically — nothing in the projects assumes the container.

`IngestionConsumer.Tests` needs a reachable SQL Server (real integration tests, not mocks — see `tests/IngestionConsumer.Tests/MergePipelineTests.cs`). Point it at one via `FAG_TEST_CONNECTION_STRING`. From inside the SDK container reaching a container on the host, use `host.docker.internal` rather than `localhost`.

Always run the full build + test suite before considering a change to `src/IngestionConsumer/Sql/`, `db/procedures/`, or `src/Shared/PathNormalizer.cs`/`HashUtil.cs` complete — those three are the ones a subtle regression is most likely to hide in silently (wrong row counts, not a compile error).

## Constraints that are load-bearing, not stylistic

These came from bugs found and fixed during initial implementation. Reintroducing any of them is a regression, not a style choice:

- **Never add an index directly on `FsObjects.FullPath`.** SQL Server's nonclustered index key limit is 1,700 bytes; `NVARCHAR(4000)` allows up to 8,000. This was tried, confirmed to fail at insert time on real paths over ~850 characters against a live SQL Server 2022 instance, and removed. All lookups go through `PathHash` (`BINARY(32)`).
- **Staging tables in `IngestionConsumer` must be local temp tables (`#`-prefixed), created per batch on the consumer's own connection — never a shared permanent table.** A shared staging table lets one consumer's cleanup step delete another consumer's in-flight rows under concurrency, silently dropping a batch. See `src/IngestionConsumer/Sql/StagingWriter.cs`.
- **`MERGE` statements in `usp_MergeFsObjectsBatch` against `SecurityDescriptors` and `FsObjects` must keep `WITH (HOLDLOCK)`.** Without it, concurrent consumers hitting the same descriptor hash at the same moment can both attempt an insert and throw a unique-constraint violation — this is a documented SQL Server behavior, not a hypothetical.
- **`PathNormalizer.Normalize` must uppercase the entire path, not just the host/share segment.** NTFS/SMB paths are case-insensitive end-to-end. Partial normalization silently creates duplicate rows for the same object under different casing. `Shared.Tests/PathNormalizerTests.cs` and `HashUtilTests.cs` have regression coverage for this specifically — don't remove those tests.
- **`ScanAgent` must call `PrivilegeEnabler.EnableBackupPrivilege()` before any file access.** Granting `SeBackupPrivilege` to the service account (Local Security Policy) is necessary but not sufficient — Windows privileges are present-but-disabled in a process token by default and must be explicitly enabled via `AdjustTokenPrivileges` at runtime.
- **Keep `/health/live` and `/health/ready` separate in `QueryApi`.** Don't collapse them back into one endpoint that checks SQL Server — that turns a transient database blip into an unnecessary Kubernetes pod restart (liveness) instead of a load-balancer removal (readiness).
- **Don't reference `System.Security.Principal.SecurityIdentifier` in `QueryApi`.** It's annotated Windows-only (CA1416) and `QueryApi` runs on Linux. `LdapSidDirectoryLookup.ParseSidToBytes` does manual SID-string parsing instead — extend that if you need more SID handling there, don't reach for `SecurityIdentifier`.

## Known verification boundary

`src/ScanAgent/Security/NativeMethods.cs` and `Win32SecurityDescriptorReader.cs` compile cleanly but have **not been run on a real Windows machine** — none was available during initial development. They're written to documented Win32/MS-DTYP behavior (`CreateFileW` + `FILE_FLAG_BACKUP_SEMANTICS` + `GetSecurityInfo` + `ConvertSecurityDescriptorToStringSecurityDescriptorW`), but treat any change in this area as needing a real Windows verification pass, not just a clean build, before trusting it. `FakeSecurityDescriptorReader` is the swap-in used for everything else (local dev on Mac/Linux, and should be used in any test that doesn't specifically target Win32 interop).

## Things intentionally not built here

Don't "complete" these without checking with the user first — they were deliberately scoped out, not forgotten:

- Full nested AD group expansion (`tokenGroups`-based closure) — `QueryApi` returns ACEs as recorded, not expanded group membership.
- The materialized reverse-index / OLAP tier for "what can user Y reach anywhere" at scale.
- Multi-agent distributed work-stealing across processes — the current `IDirectoryTaskQueue` is in-process only (`InMemoryDirectoryTaskQueue`); the schema and Kafka design already support the distributed version if it's ever needed.

Full rationale for all of the above: [`language-comparison-and-technical-design.md`](language-comparison-and-technical-design.md) §9.

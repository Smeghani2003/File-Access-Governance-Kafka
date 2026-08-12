# File Access Governance Platform

Scans Windows file systems, records who has access to what — grants and denials, inherited and direct — and answers **"who has access to this folder?"**

This repository currently implements the **Phase 1 MVP**: one file share, the scan → Kafka → SQL Server ingestion pipeline, and a read API for folder-level access lookups. The full multi-billion-object platform this is scoped from is documented in [`windows-fs-permission-scanner-plan.md`](windows-fs-permission-scanner-plan.md).

See [`ARCHITECTURE.md`](ARCHITECTURE.md) for how it fits together, and [`CLAUDE.md`](CLAUDE.md) if you're working in this repo with an AI coding agent.

## Repository layout

```
src/
  Shared/               Kafka message contracts, path normalization, hashing — referenced by every other project
  ScanAgent/             Windows Service. Walks a file share, reads security descriptors, publishes to Kafka
  IngestionConsumer/     Worker Service. Reads Kafka, bulk-loads + merges into SQL Server
  QueryApi/               ASP.NET Core Web API. Answers "who has access to folder X"
tests/
  Shared.Tests/           Unit tests (path normalization, hashing)
  QueryApi.Tests/         Unit tests (mocked repository — no database needed)
  IngestionConsumer.Tests/  Integration tests — require a real SQL Server instance
db/
  migrations/              Permanent schema (run once)
  procedures/              usp_MergeFsObjectsBatch — the ingestion merge logic
docker/
  docker-compose.yml       SQL Server + Kafka for local dev
FileAccessGovernance.sln
```

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download) — or Docker only, see below
- Docker (for local SQL Server + Kafka, and as a no-install way to build/test)
- A Windows host is required to actually **run** `ScanAgent` (it calls Win32 security APIs) — everything else runs on any OS

You don't need the .NET SDK installed locally to build or test this repo. Every command below also works via:

```bash
docker run --rm -v "$(pwd):/work" -w /work mcr.microsoft.com/dotnet/sdk:8.0 <command>
```

## Quick start

1. Start local infrastructure:

   ```bash
   cd docker
   echo "DEV_SQL_PASSWORD=Your_Dev_Password1" > .env   # not committed — see .gitignore
   docker compose up -d
   ```

2. Apply the schema:

   ```bash
   docker exec -i <sqlserver-container> /opt/mssql-tools18/bin/sqlcmd \
     -S localhost -U sa -P "$DEV_SQL_PASSWORD" -C -i /dev/stdin < db/migrations/001_initial_schema.sql
   docker exec -i <sqlserver-container> /opt/mssql-tools18/bin/sqlcmd \
     -S localhost -U sa -P "$DEV_SQL_PASSWORD" -C -i /dev/stdin < db/procedures/usp_MergeFsObjectsBatch.sql
   ```

3. Build the solution:

   ```bash
   dotnet build FileAccessGovernance.sln
   ```

4. Run the services (each needs its `appsettings.json` connection string / bootstrap servers pointed at your local containers):

   ```bash
   dotnet run --project src/QueryApi
   dotnet run --project src/IngestionConsumer
   ```

   `ScanAgent` only runs on Windows. On Mac/Linux, `Program.cs` automatically wires up `FakeSecurityDescriptorReader` instead of the real Win32 reader, so the rest of the pipeline (queue processing, Kafka publishing) can still be developed and exercised locally.

## Running tests

```bash
dotnet test FileAccessGovernance.sln
```

`Shared.Tests` and `QueryApi.Tests` are pure unit tests with no external dependencies. `IngestionConsumer.Tests` runs real integration tests against SQL Server — point it at your local container with:

```bash
export FAG_TEST_CONNECTION_STRING="Server=localhost,1433;Database=FileAccessGovernance;User Id=sa;Password=Your_Dev_Password1;TrustServerCertificate=True;"
```

## Current scope and known limitations

- **Phase 1 MVP scope**: single share, forward lookup only ("who has access to X"). Full nested Active Directory group expansion and the reverse lookup ("what can user Y reach anywhere") are out of scope here — see `language-comparison-and-technical-design.md` §9.
- **`ScanAgent`'s Win32 layer has not been runtime-verified on Windows** — it's written to documented Win32/MS-DTYP behavior and compiles cleanly, but no Windows host was available during development to actually run it. Treat it as needing a focused verification pass before production use.
- Long path support (paths exceeding ~2000 characters via the `\\?\` prefix) is not yet handled — see `FsObjects.FullPath` sizing notes in `ARCHITECTURE.md`.

# Budgeting Assistant — Build Handoff

Days 1 and 2 complete; Day 3 nearly complete. Everything committed on branch
`feat/foundation`.

**State: builds clean, 55 tests pass, app runs, seeded demo works end to end.**

What remains is the actual deploy (needs your accounts) and a browser pass over the
CSV import page. Everything else on the original plan is done.

---

## 1. Environment — read this first

Two things are NOT permanent and will bite you if you forget them.

### .NET 10 is installed to your home directory, not system-wide

`/usr/local/share/dotnet` is root-owned, so installing there needed a password.
Rather than block on that, .NET 10.0.400 (arm64) went to `~/.dotnet`.
Your system `dotnet` still resolves to **7.0.313**, which cannot build this solution.

Every session must export this before any `dotnet` command:

```bash
export PATH="$HOME/.dotnet:$HOME/.dotnet/tools:$PATH"
export DOTNET_ROOT="$HOME/.dotnet"
```

To make it permanent, add those two lines to `~/.zshrc`. Alternatively install
system-wide with `brew install --cask dotnet-sdk` (prompts for your password) and
this whole section goes away.

`dotnet-ef` 10.0.11 is installed as a global tool at `~/.dotnet/tools`.

### Docker Desktop must be running

It was not running at session start. `open -a Docker`, wait ~20s, then:

```bash
cd ~/Budgeting-Assistant && docker compose up -d
```

Postgres is exposed on **port 5433**, not 5432, to avoid colliding with any
local Postgres. Credentials are in `docker-compose.yml` and `appsettings.json`
(local dev only — production uses env vars).

---

## 2. What exists now

```
Budgeting-Assistant/
├── docker-compose.yml              pgvector/pgvector:pg17, port 5433, healthcheck
├── models/bge-micro-v2/            VENDORED embedding model (17MB) + vocab + provenance
├── docs/HANDOFF.md                 this file
├── src/
│   ├── BudgetAssistant.Core/       no EF, no ASP.NET, no ONNX — pure and testable
│   │   ├── Abstractions/IEmbedder.cs
│   │   ├── Entities/Entities.cs
│   │   └── Analysis/               Cosine, MerchantNormalizer, SimilarityBands,
│   │                               KnnCategorizer, DuplicateDetector, SubscriptionDetector
│   └── BudgetAssistant.Web/        Blazor Server + Identity + EF Core + API
│       ├── Data/ApplicationDbContext.cs
│       ├── Data/Migrations/        InitialSchema
│       ├── Data/Seed/SeedCorpus.cs 15 categories, ~90 labelled merchant strings
│       └── Services/               LocalTextEmbedder, VectorSearch
└── tests/BudgetAssistant.Core.Tests/   27 tests, ~33ms, no DB required
```

### Commits so far

```
4b11565  Add embedding service, vector search, and seed corpus
bd9b5a7  Add InitialSchema migration
456dc06  Scaffold solution with Postgres/pgvector and local embeddings
ccdf039  Initial commit
```

---

## 3. Findings that changed the plan

These were measured, not assumed. Re-read before changing anything related.

### 3.1 The similarity thresholds in the original plan were wrong

The plan guessed a 0.95 duplicate threshold and a 0.60 categorization floor.
Measured against bge-micro-v2 on normalized merchant strings:

| pair | similarity |
|---|---|
| identical string | 1.000 |
| same merchant, rotated reference code | 0.826 |
| same merchant, messy prefix | 0.856 |
| same category, different merchant | 0.612 – 0.68 |
| unrelated | 0.49 – 0.51 |

A 0.95 duplicate threshold would have matched only byte-identical strings — every
rotating-reference subscription, the exact case vectors were added for, would have
been missed. A 0.60 categorization floor would have rejected the legitimate 0.612
same-category match.

Live values are in `Core/Analysis/SimilarityBands.cs`: **SameMerchant = 0.80**,
**CategoryFloor = 0.55**. They are model-specific — **swapping the embedding model
requires re-measuring them.**

### 3.2 Normalization is load-bearing, not cosmetic

On raw descriptions the bands invert:

```
raw   "SQ *BLUE BOTTLE 4471" vs "BLUE BOTTLE COFFEE"  = 0.728   same merchant
raw   "SHELL OIL 574839201"  vs "CHEVRON 00923845"    = 0.764   DIFFERENT merchants
```

Shared digit-noise makes two unrelated gas stations look more alike than one merchant
does to itself. `MerchantNormalizer` strips that noise and separates the bands to
0.856 vs 0.612. Without it, duplicate detection is not merely weaker — it is wrong.

### 3.3 The pgvector EF plugin DOES work on EF Core 10

The plan assumed it would not (latest targets net8.0 against Npgsql EF 9) and designed
around it. That was tested and the assumption was wrong: `Pgvector.EntityFrameworkCore`
0.3.0 works with EF Core 10. It is now in use, and the design is simpler for it.

A value converter alone is not sufficient — EF needs a *store* mapping for the `Vector`
type, which only the plugin provides. Core still holds plain `float[]`; the DbContext
converts. Core therefore has no infrastructure dependency.

The `float[]` property also needs an explicit `ValueComparer`, otherwise EF compares by
reference and silently misses a recomputed embedding.

### 3.4 Security fix already applied

Swashbuckle transitively resolved `Microsoft.OpenApi` 2.3.0, which carries
**GHSA-v5pm-xwqc-g5wc (high severity)**. Pinned to 2.12.2 directly. If you add or bump
packages, re-check that `dotnet build` reports no `NU1903`.

### 3.5 SQL Server was ruled out on evidence

`mcr.microsoft.com/mssql/server` publishes a single amd64 manifest for both 2022 and
2025 — it only runs emulated on your M2. Postgres + pgvector is arm64-native and healthy
in ~4 seconds. Every job-relevant skill (EF Core Code-First, migrations, LINQ,
DbContext) is provider-agnostic; document the one-line swap in the README.

---

## 4. What is left

### Blocked on you — the deploy

Everything is deploy-ready: `Dockerfile`, `.dockerignore` and `fly.toml` are committed,
migrations run as an explicit release step, and the VM is sized at 512MB. What I cannot do
is create accounts on your behalf.

1. **Create a Postgres database** on [Neon](https://neon.tech) or
   [Supabase](https://supabase.com) — free tier, and both ship pgvector enabled, which
   Fly's own managed Postgres does not reliably. Copy the connection string.
2. **Create the Fly app** (`fly launch --no-deploy`, or `fly apps create budget-assistant`).
   Edit the `app =` line in `fly.toml` if you pick a different name.
3. **Set the secret and deploy:**
   ```bash
   fly secrets set ConnectionStrings__DefaultConnection="Host=...;Database=...;Username=...;Password=...;SSL Mode=Require"
   fly deploy
   ```
   The release step applies migrations; the app seeds itself on first boot.
4. **Optional** — turn on the Claude-written summary in place of the template:
   ```bash
   fly secrets set Anthropic__ApiKey="sk-ant-..."
   ```

### Worth doing, small

- **Click through the CSV import page in a browser.** `CsvTransactionReader` has 7 unit
  tests, but the `InputFile` binding on `/import` has never been exercised by hand. A
  ready-made file is at `/tmp/statement.csv` (it includes two deliberately bad rows to
  confirm they are reported and skipped). The Chrome extension disconnected mid-session,
  which is why this is outstanding.
- **`ZUNI CAFE` classifies as Coffee.** It is a restaurant; the word "CAFE" pulls it. The
  confidence correctly reads 0.63, so it surfaces for review rather than asserting
  certainty. Left as-is deliberately: it is honest model behaviour and a good talking
  point, not a bug to hide.
- **Consider handling transfers as their own concept.** `TRANSFER TO SAVINGS` is the only
  thing left uncategorized, correctly — it is not a merchant. A real budgeting app treats
  transfers as neither income nor spending; right now it just sits blank.

## 5. Resuming

```bash
export PATH="$HOME/.dotnet:$HOME/.dotnet/tools:$PATH"
export DOTNET_ROOT="$HOME/.dotnet"
open -a Docker && sleep 20
cd ~/Budgeting-Assistant
docker compose up -d
dotnet build && dotnet test          # expect: Build succeeded, 27 passed
dotnet run --project src/BudgetAssistant.Web
```

Inspect the database directly:

```bash
docker exec -it budgeting-assistant-db-1 psql -U budget -d budgetassistant
```

### Risk to watch

Day 3 is overloaded: CSV import, insights, polish, Docker, deploy, README. If it
slips, **cut CSV import first**. The deployed URL is the one thing that is not
negotiable — a repo link that requires cloning, installing .NET 10 and running
Docker will not get opened.

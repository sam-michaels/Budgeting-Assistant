# Budgeting Assistant — Build Handoff

Paused end of Day 1 foundation. Everything committed on branch `feat/foundation`;
working tree clean apart from a pre-existing `README.md` edit that predates this work.

**State: builds clean, 27 tests pass, database schema live.**

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

### Day 1 remainder — finish the data layer

1. **Register services in `Program.cs`.** `LocalTextEmbedder` as a singleton,
   `VectorSearch` as scoped. Neither is wired up yet.
2. **HNSW index migration**, as its own commit:
   ```sql
   CREATE INDEX ON "Transactions" USING hnsw ("Embedding" vector_cosine_ops);
   CREATE INDEX ON "MerchantExamples" USING hnsw ("Embedding" vector_cosine_ops);
   ```
   Use `migrationBuilder.Sql(...)` in an empty migration.
3. **Seeder.** Categories and `MerchantExamples` from `SeedCorpus`, each embedded
   once at startup. Then a demo user, 2–3 accounts, and ~800 transactions across
   6 months.
   - Merchant strings must stay **messy** — clean data makes the embeddings look
     pointless.
   - **Deliberately plant** what the detectors are supposed to find: a few
     same-day double charges, and 4–5 monthly subscriptions where some use rotating
     reference codes (that case is the argument for vectors over string matching).
   - Seed only when the database is empty, and never auto-apply migrations.

### Day 2 — the actual product

4. **Categorization pipeline.** On insert: normalize → embed →
   `VectorSearch.FindLabeledNeighborsAsync` → `KnnCategorizer.Suggest`. Persist
   `CategoryId`, `CategorySource`, `CategoryConfidence`. Leave uncategorized below
   the floor rather than guessing.
5. **Run the detectors** and persist `DuplicateOfId` / `IsSubscription`.
6. **API controllers + Swagger.** CRUD for accounts/transactions/categories.
   Swagger is already wired in `Program.cs`; no controllers exist yet.
7. **Blazor pages.** Dashboard (category totals, month-over-month), Transactions
   (list/edit, showing confidence and the matched example), Accounts.
   Template pages `Counter.razor` / `Weather.razor` are already deleted.
8. **More tests.** Monthly-summary and MoM math are specified but not yet written —
   `Core.Tests` currently covers the normalizer, categorizer and both detectors only.

### Day 3 — ship it

9. **CSV import** (CsvHelper is already installed). The demo moment is a reviewer
   uploading their own bank export and watching it categorize correctly.
10. **Insights endpoint.** Aggregate category totals, MoM deltas and flagged items
    into a payload containing **no raw descriptions and no PII**. With
    `Anthropic:ApiKey` set, Claude writes the narrative; without it, a deterministic
    template renders the same facts. Cache per (user, month). A missing key must
    never crash. `Anthropic.SDK` is NOT yet installed.
11. **One-click demo login.** Real Identity underneath, plus a "Try the demo"
    button that signs into the seeded account — a login wall kills recruiter
    interest. `RequireConfirmedAccount` is already set to false (there is no email
    sender).
12. **Deploy.** App on Fly.io, Postgres on **Neon or Supabase** (both ship pgvector
    enabled; Fly's own managed Postgres does not reliably). Size the VM at
    **512MB, not the free 256MB** — ONNX Runtime plus the model will not fit
    comfortably in 256MB. Migrations run as an explicit release step.
13. **Rewrite `README.md`.** It still describes the original SQL Server plan and is
    largely inaccurate. It has an uncommitted edit from before this work started.
    Document the architecture decisions in section 3 — the reasoning is the part
    worth showing an interviewer.

### Deliberately skipped

- RAG chat box — you chose categorization, which is the stronger engineering story.
- Testcontainers integration tests — stretch goal; the day-3 budget will not hold them.
- A separate `Infrastructure` project — Core is already dependency-free, which is the
  property that matters. ~15 minutes to add if you want the 4-layer split.

---

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

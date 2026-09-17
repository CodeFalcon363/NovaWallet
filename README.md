# NovaWallet Ledger Service

A wallet ledger backend for NovaPay's NovaWallet module — create wallets, credit them,
transfer funds atomically between them, and query balances/statements/audit history. Built to the
brief: money math never touches `float`/`double`, transfers are concurrency-safe and idempotent,
and the whole stack starts with `docker compose up`.

## Running it

```bash
docker compose up
```

This starts SQL Server, RabbitMQ, Redis, and the API (in that dependency order, gated on each
service's health check). On first boot the API applies EF Core migrations automatically — no
manual migration step. Verified end-to-end: full `docker compose up` from a clean state, the
walkthrough below, a 30-request burst against the rate limiter, and RabbitMQ's management API
confirming the outbox actually published (`publish_in` on the `novawallet.events` exchange).

- API: `http://localhost:8080`
- Swagger UI: `http://localhost:8080/swagger`
- RabbitMQ management UI: `http://localhost:15672` (user `novawallet` / see `docker-compose.yml`
  for the dev password)

### Getting a token

Auth is a mock/simplified issuer, exactly as the brief allows — the point is exercising the JWT
bearer middleware and claims handling, not building a real identity provider.

```bash
curl -X POST http://localhost:8080/auth/tokens \
  -H "Content-Type: application/json" \
  -d '{"customerId":"alice"}'
```

The returned `accessToken`'s `sub` claim **is** the customer ID — every wallet operation checks
that the caller's `sub` matches the wallet's owning customer.

### A full walkthrough

```bash
TOKEN=$(curl -s -X POST http://localhost:8080/auth/tokens -H "Content-Type: application/json" -d '{"customerId":"alice"}' | jq -r .data.accessToken)

curl -X POST http://localhost:8080/wallets -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" -d '{"customerId":"alice"}'
# => data.walletId

curl -X POST "http://localhost:8080/wallets/credit?walletId=<walletId>" -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" -d '{"amountMinor":500000}'

curl -X POST http://localhost:8080/transfers -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" -H "Idempotency-Key: $(uuidgen)" -d '{"sourceWalletId":"<walletId>","destinationWalletId":"<otherWalletId>","amountMinor":10000}'

curl "http://localhost:8080/wallets/statement?walletId=<walletId>&page=1&pageSize=20" -H "Authorization: Bearer $TOKEN"
curl "http://localhost:8080/wallets/audit?walletId=<walletId>&page=1&pageSize=20" -H "Authorization: Bearer $TOKEN"
```

A Postman collection covering every endpoint is at `postman/NovaWallet.postman_collection.json`.

## Running the tests

```bash
cd src/api/NovaWallet
dotnet test
```

Tests need a reachable SQL Server (see "Design decisions" below for why — the money-safety tests
specifically rely on a real engine, not a fake one). By default they target SQL Server LocalDB
(`(localdb)\MSSQLLocalDB`), which ships with Visual Studio on Windows. On macOS/Linux/CI, or to
point tests at the docker-compose SQL Server instead, set:

```bash
export NOVAWALLET_TEST_SQL_BASE="Server=localhost,1433;User Id=sa;Password=NovaWallet_Dev_Pw1!;TrustServerCertificate=True;"
```

(Start `docker compose up sqlserver` first if using this path.) Each test class gets its own
throwaway database, migrated fresh and dropped on teardown.

Tests that boot the full HTTP pipeline (anything under `Controllers/`) also need a reachable
Redis — a real one, via Testcontainers, started and torn down automatically per test run (no
setup needed beyond Docker being available to the test process).

57 tests cover every FR/NFR with a testable surface — including the concurrency edge case the
brief calls out explicitly (`TransferAsync_Under_Concurrent_Load_Never_Overdraws_Source_Wallet`),
concurrent idempotent replay, concurrent retry of a *failed* idempotency key, TTL-based key reuse,
the daily-limit WAT reset, the outbox dispatcher, and the distributed rate limiter.

## Architecture

Three projects, one dependency direction: `NovaWallet.Api` and `NovaWallet.Infrastructure` both
reference `NovaWallet.Core`; `NovaWallet.Core` references neither.

```
NovaWallet.Api             HTTP concerns only: controllers, auth, middleware, DI wiring.
  Controllers/               Thin — bind request, call a Core service, wrap the result.
  Security/                  JWT issuance + validation, rate-limiter policy names, Redis options.
  Middleware/                RFC 7807 exception handler, correlation ID, request logging.
  HealthChecks/               Liveness/readiness (SQL Server, RabbitMQ, Redis).
  BackgroundServices/         Outbox dispatch + idempotency-key cleanup loops (thin — delegate to Core).

NovaWallet.Core             Everything that isn't an HTTP or infrastructure concern.
  Entities/                   EF Core entities, DataAnnotations-validated.
  Data/                       DbContext, migrations, SQL Server exception classification.
  Repositories/               EF Core — the write side.
  Queries/                    Dapper — the read side (see "CQRS-lite" below).
  Services/                   WalletService, TransferService — the actual business logic.
  Exceptions/                 Domain exceptions carrying their own HTTP status code.
  Models/                     Request/response DTOs.

NovaWallet.Infrastructure    Genuinely external integrations: the RabbitMQ publisher.

NovaWallet.Test              xUnit. Real SQL Server + Redis fixtures shared per test collection.
```

### Design decisions and trade-offs

Every non-trivial decision below follows the same shape: **Reason** it was made this way,
**Alternative(s)** genuinely considered, and the **Trade-off** accepted by not choosing them.
Three of these (marked ⚠) were caught mid-build as the *wrong* call for a system at this scale and
corrected — the "before" is included because the reasoning behind the fix is more informative than
just the final state.

#### Money & data model

**Integer kobo (`long`) everywhere, never `float`/`double`.**
- *Reason:* the brief's hard constraint — floating-point representation error compounding across
  many transactions is exactly the class of bug a ledger cannot tolerate.
- *Alternative:* `decimal` (.NET's base-10 fixed-point type) would also avoid binary
  floating-point error and is a common choice for money.
- *Trade-off:* `decimal` is a legitimate alternative for arbitrary-currency systems with varying
  minor-unit counts, but adds a division/rounding step (kobo ↔ naira) at every boundary for no
  benefit here — NGN's minor unit is fixed and integer kobo *is* the brief's stated wire format.
  Skipping `decimal` avoids a conversion step this system never needs.

**One wallet per customer**, enforced by a unique index on `Wallet.CustomerId` (409 on a second
`POST /wallets`).
- *Reason:* matches the brief's phrasing ("create a wallet for a customer id") and this session's
  explicit direction; avoids the extra "which wallet?" resolution every endpoint would otherwise
  need.
- *Alternative:* multiple wallets per customer (e.g. a main wallet plus goal-based sub-wallets, as
  NovaSave's product surface might eventually need).
- *Trade-off:* the current schema would need a migration (drop the unique constraint, add a
  wallet-selection concept to every endpoint) to support that later — accepted because building
  multi-wallet support with only one real caller (this ledger) and no stated multi-wallet
  requirement would be speculative.

**Three-project layering, one dependency direction** (`Api`/`Infrastructure` → `Core` → nothing).
- *Reason:* explicit direction this session — `.Core` must never reference another project, so
  domain logic stays testable and hosting-framework-free.
- *Alternative:* a single project, or a four-layer split (e.g. a separate `Domain` below `Core`).
- *Trade-off:* three projects is more ceremony than one for a service this size, but it's what
  makes `NovaWallet.Test` able to unit-test `WalletService`/`TransferService` without spinning up
  ASP.NET Core hosting at all. A fourth layer was rejected as unearned — nothing in this system
  needs `Core` split further.

#### Persistence & query strategy

**SQL Server**, not PostgreSQL/MySQL/SQLite.
- *Reason:* explicit direction this session.
- *Alternative:* PostgreSQL was the natural runner-up — free, and `SELECT ... FOR UPDATE` /
  `SERIALIZABLE` are at least as capable as SQL Server's equivalents for this workload.
- *Trade-off:* SQL Server's native `rowversion` column type is what backs the optimistic-
  concurrency tokens on `Wallet` and `TransferIdempotencyRecord` — it's automatically maintained
  by the engine on every row update, so there's no code path that can forget to bump a version
  number. Postgres would need an app-managed integer/timestamp column instead, which is more
  portable but reintroduces exactly the kind of manual bookkeeping a concurrency token exists to
  avoid getting wrong.

**CQRS-lite: EF Core for writes, Dapper for reads.**
- *Reason:* explicit direction this session — matches the scaffold's pre-existing
  `Repositories/` (EF) vs `Queries/` (Dapper) folders.
- *Alternative:* EF Core for everything (simpler — one query technology to know), or a genuinely
  separate read store (a projection/read-replica) for true CQRS.
- *Trade-off:* running two query technologies means two things to keep consistent (e.g. the enum
  string-conversion in `OnModelCreating` has to match what the raw Dapper SQL expects). A fully
  separate read store was rejected as needing an actual read-scaling problem to justify — this
  system doesn't have one; Dapper here is just "skip EF's change-tracking cost on read-only,
  paginated, indexed queries," not "a different database."

**Repository pattern**, not a generic `IRepository<T>`.
- *Reason:* explicit direction this session (Repository pattern), refined during implementation
  to per-aggregate interfaces (`IWalletRepository`, `IIdempotencyRepository`, …) instead of one
  generic type.
- *Alternative:* a single `IGenericRepo<T>` with `Add`/`Update`/`Remove`/`GetQueryable` — this
  exact pattern was actually found, already written, elsewhere in the initial repo scaffold
  (referencing a DbContext from an unrelated prior project) and was removed rather than reused.
- *Trade-off:* a generic repository is less code to write once, but it either leaks
  `IQueryable<T>` (defeating the point of hiding persistence details behind the interface) or
  forces every specialized query (`TryClaimForRetryAsync`'s RowVersion-guarded update,
  `GetUnprocessedAsync`'s ordered batch) into ad hoc `Expression<Func<T,bool>>` parameters that
  are harder to read than a named method. Per-aggregate interfaces are more files, but every
  method name says exactly what business operation it serves.

#### Concurrency & idempotency

**Optimistic concurrency (RowVersion + retry), not pessimistic row locking, for wallet balance
updates.**
- *Reason:* explicit direction this session, with a concrete rationale: wallets are tied to
  individual phones, so two genuinely simultaneous debits on the *same* wallet are rare in
  production — paying a per-transfer locking cost for contention that mostly doesn't happen
  wasn't worth it.
- *Alternative:* `SELECT ... FOR UPDATE`-equivalent pessimistic locking — simpler to reason about
  in isolation, and immune to retry-budget tuning.
- *Trade-off (⚠ tuned after a real failure):* the first cut of `ConcurrencyRetry` used 5 attempts
  with no delay between them. The concurrency test — 20 concurrent credits against one wallet —
  failed immediately: every loser retried at the same instant as every other loser, so most
  rounds just re-collided instead of one request getting a clear shot at the row. Fixed with
  jittered, attempt-scaled backoff (`Random.Shared.Next(5, 20) * attemptNumber` ms) and a 10-attempt
  budget; the same retry helper was then applied to the `DailyOutboundUsage` insert race and the
  idempotency claim path too, rather than only patching the one path with a failing test. Full
  writeup in `AI_USAGE.md`.

**Idempotency is reserve-then-complete via a database unique constraint, not a distributed lock
or a cache.**
- *Reason:* `Idempotency-Key` becomes the primary key of `TransferIdempotencyRecord`; a second
  concurrent `INSERT` with the same key fails atomically at the database rather than relying on
  an application-level check-then-write (which has the textbook race: two concurrent retries can
  both pass a "does this exist?" check before either commits).
- *Alternative:* a distributed lock (e.g. Redis `SETNX`) around a check-then-process block, or a
  cache (e.g. Redis) holding key → result.
- *Trade-off:* a cache would need its own durability/eviction story and a second source of truth
  to keep consistent with the actual transfer outcome; a distributed lock adds an extra
  infrastructure dependency and a lock-expiry policy to get right (expire too early and two
  requests can still both hold the "lock" briefly; too late and a crashed holder blocks everyone).
  The unique-constraint approach piggybacks on a guarantee the database already provides for
  free.
- *(⚠ found and fixed mid-build):* the original design treated a `Failed` record as immediately,
  unconditionally safe to retry — call `ProcessTransferAsync` directly, no re-reservation. Under
  *concurrent* retries of the same failed key, multiple callers could all observe `Failed` and all
  fall through to actually processing the transfer simultaneously: each debit is individually
  protected by the wallet's `RowVersion`, but nothing stopped two full transfers from both
  succeeding. Fixed by routing every retry (Failed, or an expired Completed record) through
  `TryClaimForRetryAsync` — the same `RowVersion`-guarded `UPDATE` pattern as the wallet balance
  itself, so only one concurrent claimant wins and the others loop back to re-check. See
  `AI_USAGE.md` for the full trace of how this was found.

**Idempotency keys expire after a configurable TTL (`IdempotencyKeyTtl:Hours`, default 24).**
- *Reason:* bounds `TransferIdempotencyRecord` storage growth and — more importantly — bounds how
  long a `Failed` or `Completed` key blocks reuse with a different payload. Without a TTL, a typo'd
  request that failed permanently would tie up its idempotency key forever.
- *Alternative considered and rejected:* expiring `Pending` records the same way, so a
  crashed-mid-flight request doesn't block that key forever either.
- *Trade-off (deliberately not applied — this is the "alternative that didn't fit" case worth
  flagging explicitly):* a `Pending` record can never be distinguished, from the database's point
  of view, between "the process crashed and this is abandoned" and "this is a slow request that
  is still legitimately in flight." Treating an expired `Pending` row as reclaimable would let a
  second request start processing while the first might still complete — a real double-spend risk,
  not just a stale-cache problem. `Pending` rows are therefore explicitly exempt from TTL-based
  reuse in `TransferService`'s decision table; they only ever resolve by the original request
  finishing (or by the poller giving up after `MaxIdempotencyPollAttempts` and returning a 409).
  A stuck `Pending` key past its nominal TTL stays stuck — see "Known limitations."
- Cleanup itself is a `BackgroundService` (`IdempotencyCleanupBackgroundService`, hourly) that
  bulk-deletes expired `Completed`/`Failed` rows via `ExecuteDeleteAsync` — never touches `Pending`.

#### Daily limit & time handling

**`DailyOutboundUsage` is an incrementally-maintained counter, not a per-transfer `SUM()`.**
- *Reason:* updated in the same transaction as the transfer debit, avoiding an aggregate query
  over the whole day's transactions on every single transfer.
- *Alternative:* `SUM(AmountMinor) WHERE WalletId = ... AND CreatedAtUtc >= today` computed fresh
  each time.
- *Trade-off:* the counter needs its own row (and its own insert race — a genuine one, since two
  concurrent first-transfers-of-the-day both try to `INSERT` the same `(WalletId, UsageDateWat)`
  row) rather than being derivable purely from `LedgerTransaction`. `ConcurrencyRetry` treats a
  unique-constraint violation on that insert the same as a `RowVersion` conflict — reload and
  retry — so this doesn't need separate handling.

**WAT is a fixed UTC+1 offset, not an IANA timezone lookup (`Africa/Lagos`).**
- *Reason:* Nigeria has never observed daylight saving time, so "midnight WAT" is always exactly
  "23:00 UTC the day before" — a fixed offset is correct with zero ambiguity.
- *Alternative:* `TimeZoneInfo.FindSystemTimeZoneById("Africa/Lagos")`, which would keep working
  automatically if that ever changed.
- *Trade-off:* `TimeZoneInfo` lookups need an IANA tzdata source available at runtime, which isn't
  guaranteed inside every minimal Linux container image without an explicit package — a real
  failure mode for zero practical benefit, since the premise (Nigeria adopts DST) hasn't happened
  and isn't expected to.

#### Eventing

**Outbox pattern + RabbitMQ, not a direct publish from the request path.**
- *Reason:* every balance mutation writes a domain event to an `OutboxMessage` row in the *same*
  database transaction as the mutation, so the event can never be "lost" relative to the DB state
  even if the broker is briefly unreachable — a `BackgroundService` polls and publishes
  independently, so a broker hiccup doesn't block the client's response.
- *Alternative:* publish directly to RabbitMQ inside the request, or Kafka / Azure Service Bus /
  AWS SNS instead of RabbitMQ, or a framework like MassTransit to manage the outbox instead of a
  hand-written one.
- *Trade-off:* a direct publish is simpler but couples the client's response time to the broker's
  availability and loses the "committed to DB but never published" case entirely. Kafka/Azure
  Service Bus are heavier infrastructure than this system's actual throughput justifies. MassTransit
  would have handled retries/outbox mechanics for free, at the cost of a framework dependency and
  its own learning curve for anyone reading this code — the hand-written version here is ~40 lines
  and every line is either domain logic or a direct `RabbitMQ.Client` call, nothing hidden.

**Outbox dispatch commits the whole batch in one `SaveChangesAsync`, not one per message (⚠
fixed).**
- *Reason:* the original version called a repository method per message (`MarkProcessedAsync` /
  `IncrementAttemptsAsync`), each doing its own `FindAsync` + `SaveChangesAsync` — N round trips
  for a batch of N, flagged while checking the codebase for exactly this pattern.
- *Alternative kept for comparison:* per-message commits give tighter fault isolation — if the
  process crashes mid-batch, messages already marked processed before the crash stay marked
  processed.
- *Trade-off:* under the system's already-accepted at-least-once/idempotent-consumer contract,
  that isolation isn't actually needed — a message published-but-not-yet-committed at crash time
  simply gets republished on the next tick, which downstream consumers must already tolerate.
  Batching to one `SaveChangesAsync` per dispatch cycle removes the N-round-trip cost with no
  correctness cost given that existing contract.

#### Rate limiting

**Redis-backed distributed sliding window, not ASP.NET Core's in-memory fixed-window limiter (⚠
fixed).**
- *Reason:* the original implementation used `AddFixedWindowLimiter` — in-memory, per-process.
  Flagged during review: with N horizontally-scaled API instances behind a load balancer, each
  instance keeps its own independent counter, so the *effective* global limit becomes N × 20/10s,
  not 20/10s — the limiter would silently stop doing its job under the exact production topology
  (multiple instances) a real deployment of this service would use. Fixed window also has a
  separate, smaller issue even single-instance: a client can send the full permit count in the
  last moment of one window and again in the first moment of the next, briefly doubling the
  intended rate.
- *Alternative:* keep in-memory but switch to `AddSlidingWindowLimiter` (closes the boundary-burst
  gap, but not the multi-instance gap); or a distributed lock/Lua-script implementation
  hand-written against `StackExchange.Redis` directly.
- *Trade-off:* the [`RedisRateLimiting`](https://www.nuget.org/packages/RedisRateLimiting) package
  implements the same `System.Threading.RateLimiting` abstractions ASP.NET Core already uses,
  backed by a sliding-window Lua script in Redis — adopting it closes both gaps at once for one
  new (well-established, actively maintained) dependency, versus hand-rolling Lua scripts for
  marginal benefit over a library built for exactly this. Cost: the transfer endpoint now has a
  hard runtime dependency on Redis being reachable, which is why Redis also gets its own
  `/health/ready` check.

#### Auth, validation & API contract

**JWT bearer auth with a mock/simplified issuer**, not a real identity provider.
- *Reason:* the brief explicitly scopes this out — "the point is the middleware and claims
  handling, not building a full auth server."
- *Alternative:* wire up a real OIDC provider (even a lightweight one like Duende IdentityServer
  or a hosted option) for a more realistic token lifecycle (refresh tokens, revocation).
- *Trade-off:* a mock symmetric-key (HS256) issuer has no refresh/revocation story and a single
  static signing key checked into `appsettings.json` (clearly labeled dev-only) — acceptable
  because building real identity infrastructure was explicitly out of scope, but a genuine gap if
  this code were ever mistaken for production-ready auth.

**Attribute-based (`DataAnnotations`) validation, not FluentValidation.**
- *Reason:* explicit direction this session — validation rules here (`[Required]`, `[Range]`,
  `[StringLength]`, `[RegularExpression]`) are simple enough that a separate validator library is
  an unearned abstraction; `DataAnnotations` integrates with ASP.NET Core's model binding for
  free, with no per-request validator-object allocation.
- *Alternative:* FluentValidation — better for complex, composable, or cross-field rules, and
  independently unit-testable without an HTTP context.
- *Trade-off:* `DataAnnotations` attributes are harder to unit-test in isolation (they're
  naturally exercised through the model-binding pipeline, i.e. integration-style) and don't
  compose well for rules that span multiple properties. None of this system's validation rules
  need that, so the simpler option was kept.

**Query-param-only routing** (`?walletId=`), never a path segment for a resource identifier.
- *Reason:* explicit direction this session.
- *Alternative:* conventional REST-style paths (`GET /wallets/{id}`, `GET /wallets/{id}/statement`).
- *Trade-off:* path-based routing is more idiomatic REST and reads slightly cleaner in tooling
  that assumes it (e.g. some API gateways' path-based rate-limit/auth rules) — not a cost that
  mattered here, and the brief's own hard constraints don't require either style.

**A uniform `{status, message, data}` envelope for success responses, RFC 7807 Problem Details
for errors — not one shape for both.**
- *Reason:* explicit direction this session (uniform envelope) reconciled with the brief's hard
  constraint that errors use RFC 7807 — forcing errors into `{status, message, data}` would lose
  the standard `type`/`title`/`detail`/`instance` fields and fail that constraint as written.
- *Alternative:* wrap error bodies in the same envelope too (`data` holding the problem details),
  for one shape everywhere.
- *Trade-off:* one universal shape is simpler for a client to parse, but violates the brief's
  explicit RFC 7807 requirement — scoping the envelope to success and keeping RFC 7807 for errors
  satisfies both directives instead of picking one over the other.

**The success envelope is built by a shared `NovaWalletControllerBase.Success<T>()` helper, not a
global `IAsyncResultFilter`.**
- *Reason:* every controller action calls one shared method rather than hand-constructing
  `{status, message, data}` inline — the goal (no accidentally-inconsistent shape) without adding
  a filter to the request pipeline.
- *Alternative:* a global result filter that inspects the return value after every action and
  wraps it automatically — more "automatic" (impossible to forget even in a future controller).
- *Trade-off:* a filter is one more pipeline component to understand when reading the request flow
  end-to-end, for a guarantee a shared base-class method already gives as long as controllers stay
  thin (which is itself an explicit rule here) — the helper was judged simpler for the actual size
  of this API surface (6 controllers).

**One `NovaWalletExceptionHandler` that classifies exceptions by type, not one handler per
exception type.**
- *Reason:* `NovaWalletDomainException` carries its own `StatusCode`, so mapping *any* domain
  exception to a response is a one-line `exception is NovaWalletDomainException`
  check — a chain of per-type handlers would just be repeating that dispatch logic per exception
  class for no behavioral difference.
- *Alternative:* ASP.NET Core's `IExceptionHandler` supports registering multiple handlers, tried
  in order — one per exception type, each returning `false` to fall through if it doesn't match.
- *Trade-off:* multiple handlers would scale better if each exception type needed genuinely
  different handling logic (different headers, different logging, different retry hints) — none
  of them do here; they all become the same `ProblemDetails` shape with a different status
  code/message, so one handler with a switch expression is less code for the same outcome.

#### Observability & health

**Correlation ID: middleware-generated, propagated via `HttpContext.Items`, re-applied by the
exception handler.**
- *Reason:* one ID per request, attached to logs, audit entries, and outbox events, so a single
  request's full trail (including its downstream event) can be found by one ID.
- *Alternative:* rely on ASP.NET Core's built-in `HttpContext.TraceIdentifier` instead of a custom
  header/middleware.
- *Trade-off:* `TraceIdentifier` isn't controllable by the caller (can't be supplied via an inbound
  header for cross-service trace continuity) and isn't automatically surfaced in the response —
  the custom middleware honors an inbound `X-Correlation-Id` if present and always echoes it back.
  *(A related bug, not a trade-off: the correlation-ID accessor originally minted a new GUID on
  every access if the middleware hadn't run, meaning two audit entries for the same mutation could
  silently get two different correlation IDs. Fixed to compute the fallback once and cache it back
  into `HttpContext.Items`.)*

**Liveness (`/health`) does zero dependency checks; readiness (`/health/ready`) checks SQL Server
+ RabbitMQ + Redis, each with a fresh connection attempt.**
- *Reason:* an orchestrator should restart the process on liveness failure (nothing to check but
  "is it running") but only pull it from rotation, not restart it, on readiness failure
  (dependencies recover on their own) — conflating the two would cause restart loops when a
  downstream dependency is merely slow to come up.
- *Alternative:* one combined `/health` endpoint, or readiness checks that reuse a long-lived
  pooled connection instead of attempting a fresh one each probe.
- *Trade-off:* a fresh connection per readiness probe costs a bit more than reusing a cached one,
  but answers the question an orchestrator actually needs answered — "would a *new* request to this
  dependency succeed right now" — rather than "was the connection healthy whenever it was last
  used."

#### Testing strategy

**Real SQL Server for every persistence-touching test, not SQLite or the EF Core InMemory
provider.**
- *Reason:* this system's money-safety guarantees are specifically about SQL Server behavior —
  unique-constraint exception *shape* (`SqlException.Number` 2627/2601, used to translate a
  duplicate-customer insert into a 409) and `RowVersion`/`rowversion` concurrency semantics. Both
  differ from or are absent in SQLite and are entirely unmodeled by the InMemory provider (which
  doesn't enforce most relational constraints at all).
- *Alternative:* SQLite for speed, InMemory for zero external dependency, or Testcontainers for a
  real disposable SQL Server per test run.
- *Trade-off:* SQLite/InMemory would be faster and need no local SQL Server/LocalDB, but a test
  suite that "passes" against a fake engine while the real one behaves differently is worse than
  no test at all for exactly the properties this brief cares most about. LocalDB (default) or a
  configurable real SQL Server (`NOVAWALLET_TEST_SQL_BASE`, for macOS/Linux/CI) was chosen over
  Testcontainers.MsSql for the SQL side specifically to avoid a Docker dependency for local
  Windows development, where LocalDB is already available — Redis, added later, uses Testcontainers
  directly since there's no LocalDB-equivalent zero-install option for it and Docker had become
  available in this environment by then.

#### Deliberately not built

- **A reconciliation job** comparing this ledger against an external payment processor's records.
  Standard practice for real payment systems (don't trust the request/response path alone —
  network failures are a first-class case, not an edge case) but there is no external payment
  processor in this system's actual scope: `POST /wallets/credit` *simulates* an inbound NIP
  transfer rather than calling out to NIBSS, so there is nothing external to reconcile against yet.
  The idempotency design (durable key → result, safe replay) is the mechanism that would make a
  future reconciliation job's *corrective* actions safe to apply, without needing the job itself
  today.
- **A saga orchestrator.** Reserved for a future workflow spanning *multiple services with their
  own databases* (e.g. a NovaLend disbursement crediting this ledger). Both wallets in a transfer
  live in one shared database today, so the transfer uses a plain ACID transaction — building a
  saga for a same-database, two-row update would be the overengineering this project was
  explicitly steered away from.

### API surface

No route uses a path parameter to identify a resource — every identifier is a query string
parameter (`?walletId=...`). Every success response is wrapped in a uniform envelope:

```json
{ "status": 200, "message": "Balance retrieved", "data": { } }
```

Every error response is RFC 7807 Problem Details (`application/problem+json`) — the two are
scoped to success vs. failure respectively, not mixed.

| Method & Path | Auth | Notes |
|---|---|---|
| `POST /auth/tokens` | — | Mock token issuance, body: `{customerId}` |
| `POST /wallets` | JWT | body: `{customerId}` |
| `GET /wallets?walletId=` | JWT | |
| `POST /wallets/credit?walletId=` | JWT | body: `{amountMinor}` |
| `POST /transfers` | JWT | header `Idempotency-Key`; rate-limited (20/10s, distributed via Redis); body: `{sourceWalletId, destinationWalletId, amountMinor}` |
| `GET /wallets/statement?walletId=&page=&pageSize=` | JWT | paginated, newest first |
| `GET /wallets/audit?walletId=&page=&pageSize=` | JWT | separate from the statement table, per the brief |
| `GET /health` | — | liveness |
| `GET /health/ready` | — | readiness — checks SQL Server + RabbitMQ + Redis |

### Security

- JWT bearer auth on every business endpoint; a caller can only act on/view a wallet whose
  `CustomerId` matches their token's `sub` claim.
- All request DTOs use `DataAnnotations` (`[Required]`, `[Range]`, `[StringLength]`,
  `[RegularExpression]`), validated by ASP.NET Core's built-in model validation — deliberately not
  a separate validator library, to avoid the extra allocation and abstraction for validation
  rules this simple.
- Entities are validated a second time in `NovaWalletDbContext.SaveChanges` (defense in depth
  against an invalid state reaching the database via any path other than the DTO).
- Every audit entry records who (actor claim), what (action), when, balance before/after, and the
  caller's session IP (honoring `X-Forwarded-For` behind a proxy).
- Security response headers: CSP, X-Frame-Options, X-Content-Type-Options, X-XSS-Protection,
  Referrer-Policy, HSTS. The `Server` header is suppressed rather than disclosed.
- No secrets are hardcoded outside of the `appsettings.json` dev-only defaults (clearly labeled);
  in a real deployment these come from environment variables / a secret store.
- Rate limiting (20 requests/10s, Redis-backed sliding window — correct under horizontal scaling,
  see "Design decisions") on `POST /transfers`.

### Known limitations / assumptions

- The JWT issuer is intentionally mock — no real identity verification, no refresh tokens.
- A crashed request mid-transfer (a genuine process crash, not a caught exception) can leave an
  idempotency key stuck `Pending` indefinitely. This is unaffected by the TTL mechanism —
  deliberately: an expired `Pending` record still can't be safely distinguished from "abandoned"
  vs. "still legitimately processing," so it is never auto-reclaimed (see "Design decisions" →
  idempotency TTL). A future replay with that exact key polls and eventually returns a 409 rather
  than reprocessing. A client hitting this picks a new idempotency key.
- No reconciliation job exists against an external source of truth, because there isn't one in
  this system's scope yet — see "Deliberately not built."
- Verified end-to-end via `docker compose up` from a clean state in this environment (build,
  health-gated startup order, migration-on-boot, a full wallet→credit→transfer→statement→audit
  walkthrough, a 429 burst against the live rate limiter, and RabbitMQ's management API confirming
  the outbox published). One fix was needed to get there:
  `rabbitmq:3.13-management`'s default entrypoint failed on first boot with
  `Error when reading /var/lib/rabbitmq/.erlang.cookie: eacces` on this Docker Desktop setup;
  resolved by pinning `RABBITMQ_ERLANG_COOKIE` explicitly in `docker-compose.yml` rather than
  letting the image generate one on a volume with permissions that didn't work here.

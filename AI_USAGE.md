# AI Usage

I built this service with Claude Code (Anthropic's agentic CLI, running Claude Sonnet 5) doing
the actual implementation under my direction — I set the architecture constraints, made the
blocking design calls, and reviewed/ran everything it produced before committing. This file
documents that process honestly, including where the AI got it wrong.

## Tools used

- **Claude Code** — the only AI tool used. It wrote the entities, services, controllers,
  middleware, tests, and docker-compose configuration; ran `dotnet build`/`dotnet test` in a loop
  to verify its own work before I let it commit; and used `dotnet ef migrations` to generate the
  schema. I used it as a pair programmer I stayed adversarial with, not an autopilot.

## Representative prompts

**1. Setting the architecture constraints, before any code was written:**

> "if a simple solution solves a problem effectively and efficiently, NEVER OVERENGINEER. In this
> project I am using Repository pattern, Outbox pattern, Event driven system, Message queue, Saga
> pattern and eventual consistency if db is shared. THIN CONTROLLERS is a must - no business
> logic in controller. use Dapper for reads and Ef core for writes. use indexing. look out for DB
> transactions or schemas that introduce DB overheads and unecessary N+1."

What came back: a CQRS-lite split (EF Core repositories for writes, Dapper queries for reads,
matching the scaffold's existing `Repositories/`/`Queries/` folders), thin controllers that only
bind + call a Core service, and — correctly — no saga orchestrator, since it reasoned that both
wallets in a transfer live in the same database and a plain ACID transaction covers that case; a
saga would only be justified if the ledger were ever split across services with separate
databases. I agreed with that call and didn't have to push back on it.

**2. Directing the implementation phase with hard requirements:**

> "Create a testcase for every method or feature that fufils a business/functional/non functional
> requirement. test thoroughly before you commit. All automated test case must pass. Watch out
> for methods that creates N+1 or unnecessary EF overhead. Commit incrementally i.e when you
> complete a self contained feature. all projects may reference .Core but .Core do not reference
> any other project. DO NOT OVERENGINEER."

What came back: eight incremental commits (data model → auth/envelope → credit → transfer with
concurrency + idempotency → daily limit → statement/audit/outbox → hardening), each with its own
tests run and passing before the commit happened. It also caught and removed leftover scaffold
code in the repo (a `CashInflow.Core` namespace with a generic-repository pattern and a reference
to a DbContext that didn't exist in this project) rather than silently working around it — that
wasn't something it generated, but it correctly flagged it as contamination from a different
template rather than leaving it in.

**3. Mid-session correction on a concurrency design assumption:**

> "if this speaks to inter wallet transfer between one customer to another customer, optimistic
> concurrency should be best because. its not like a GL where many transaction are hitting at the
> same time. wallets are tied to phones and only in uncommon cases will debits be hitting same
> time."

This is what settled optimistic concurrency (a `RowVersion` token + retry) over row-level locking
for the transfer path. It's a real trade-off, not a free lunch — see the bug below, which is
exactly the failure mode that trade-off exposed under test.

**4. Pushing on a design decision after it was already implemented:**

> "have you considered this in a distributed system where more than 1 app instance is running,
> every architecture decision should counsider that."

Said in response to a rate-limiting algorithm already wired up and working in tests — the
question wasn't about the algorithm at all, it was about whether an in-memory limiter makes sense
once you assume more than one instance. It didn't. See finding #4 below.

## Where the AI's output was wrong, and how I caught it

### 1. Concurrency bug: the retry budget wasn't sized for its own test

The first version of the optimistic-concurrency retry loop (`ConcurrencyRetry.ExecuteAsync`) used
5 attempts with no delay between them. To actually prove the "balance never goes negative under
concurrent load" requirement rather than just asserting it, I had it write a test that fires 20
concurrent credit requests at the same wallet — and that test failed immediately, with
`DbUpdateConcurrencyException` surfacing as an unhandled error on several of the 20 requests. Five
instant retries with no backoff meant every losing request retried at the same moment as every
other loser, so most rounds just re-collided instead of one of them getting a clear shot at the
row.

This is precisely the kind of concurrency bug the brief warns about: it would have shipped clean
in casual manual testing (a person clicking a button twice doesn't generate real contention) and
only shown up under genuine concurrent load — exactly the scenario a financial ledger has to hold
up under. The fix was to add jittered, attempt-scaled backoff (`Random.Shared.Next(5, 20) *
attemptNumber` ms) and raise the budget to 10 attempts. I made it re-run the same test five times
in a row after the fix to confirm it wasn't just passing by luck, and then applied the same
lesson to the transfer path and the daily-outbound-usage counter's insert race before moving on,
rather than only patching the one path that had a failing test.

### 2. Stale-read bug in the idempotency poller (the more dangerous one)

When I extended the concurrency test to a genuinely concurrent scenario — 8 parallel requests
replaying the *same* new `Idempotency-Key`, which should all block on the one that wins the race
and then return its result — 7 of the 8 requests timed out with "still processing" instead of
returning the completed transfer.

The cause: `IdempotencyRepository.GetAsync`, used by the polling loop to check whether the
in-flight request had finished, was written with EF Core's `FindAsync`. `FindAsync` checks the
`DbContext`'s local identity-map cache before it queries the database — so a poller's *first*
call correctly saw `Pending`, but every subsequent call in the same polling loop, running on the
same `DbContext`, returned that same cached `Pending` instance forever, no matter what the
database actually said. The poller was looking at a fixed sub with no way to see the winner
finish.

This one worried me more than the retry-budget issue, because it's silent: it would look
completely correct in any test or manual check that doesn't specifically exercise concurrent
replay of a brand-new key, and in production it would surface as idempotent retries from a flaky
mobile client occasionally hanging for no visible reason — hard to reproduce, hard to diagnose. I
had it fix `GetAsync` to use `AsNoTracking()` with a direct query instead of `FindAsync`, which
forces every poll to hit the database rather than the cache, and left a comment explaining why
that specific method needs to bypass the identity map when every other read in the codebase
correctly benefits from it.

### 3. A naive default that would have broken every authorization check

Separately (not concurrency, but worth including as a second category of "wrong"): the first pass
at JWT authentication read the caller's identity via
`User.FindFirst(JwtRegisteredClaimNames.Sub)`. That's the obviously "correct"-looking way to read
a `sub` claim, and it's wrong by default in ASP.NET Core — the JWT bearer handler silently remaps
`sub` to `ClaimTypes.NameIdentifier` unless `MapInboundClaims = false` is set on the bearer
options. Every wallet-ownership check (`caller must match the wallet's CustomerId`) was reading a
claim that no longer existed under that name, so every authenticated request was being rejected
as Forbidden — including ones with a perfectly valid token for the wallet's actual owner. This
surfaced immediately as an integration-test failure (expected 201, got 403) rather than shipping
silently, which is the outcome you want from a test suite, but it's a good example of AI-generated
code confidently doing the textbook-looking thing while missing a framework-specific gotcha.

### 4. Rate limiting that silently stops working under the deployment topology it's meant for

This one I didn't catch — I asked which rate-limiting *algorithm* the transfer endpoint should
use (fixed window vs. sliding window vs. token bucket), expecting a discussion about burst
behavior at window boundaries. The answer pointed out a bigger problem with the question itself:
ASP.NET Core's built-in rate limiter is in-memory and counts requests per *process*. With N
horizontally-scaled API instances behind a load balancer — the normal shape of a real deployment,
not an edge case — each instance keeps its own independent counter, so the actual global limit
becomes N × the configured value, not the configured value. The limiter would pass every test run
against a single instance and then silently stop enforcing anything once the service actually
scaled out, which is exactly when a money-movement endpoint most needs it enforced.

I had it replace the in-memory limiter with a Redis-backed sliding window
([`RedisRateLimiting`](https://www.nuget.org/packages/RedisRateLimiting), which implements the
same `System.Threading.RateLimiting` abstractions ASP.NET Core already uses) so the limit is a
single count shared across every instance. This one is worth including precisely because I was
the one who almost let it through — I'd framed the question narrowly (algorithm choice) and the
AI answered the question I actually should have asked (does this design survive more than one
instance) instead of the one I asked. That's a case worth naming honestly rather than only listing
the bugs I personally caught.

### 5. A second concurrency race, found while fixing something else

Adding a TTL to idempotency keys (so a permanently-failed key doesn't block reuse forever) meant
touching the code path for retrying a `Failed` transfer. Re-reading it to add the TTL check
surfaced a bug already sitting there: a `Failed` idempotency record was treated as unconditionally
safe to retry immediately — call the transfer logic directly, no reservation step. Under
*concurrent* retries of the same failed key, multiple callers could all observe `Failed`
simultaneously and all fall through to actually processing the transfer. Each individual debit is
still protected by the wallet's `RowVersion` token, so no single debit could double itself — but
nothing stopped two *separate, complete* transfers from both succeeding for what was supposed to
be one logical retried operation. I closed it by routing every retry through the same
`RowVersion`-guarded claim pattern already used for the wallet balance itself, so only one
concurrent claimant wins.

I'm including this one specifically because it wasn't found by a failing test — it was found by
re-reading code that already had passing tests, while working on an unrelated feature. The lesson
isn't "trust the test suite less," it's that a concurrency bug can sit in already-shipped,
already-"working" code indefinitely if nothing ever generates the specific interleaving that
exposes it, which is exactly why re-reading adjacent code while making any change to a
money-movement path is worth the time it costs.

## What this says about directing AI on a financial system

None of these five findings were things a quick glance at the code would have caught. Three
surfaced because I insisted on tests that actually exercise concurrency and full request
round-trips rather than accepting "the code looks right" and the happy path passing; one surfaced
because re-reading adjacent code while changing something else is worth the time; and one — the
one I'm least comfortable with — surfaced only because I happened to ask a broader question than
the one I'd originally intended, not because I'd already thought to check for it. That last one is
the real judgment call this brief is testing: AI is fast at producing plausible-looking code for a
domain like this, and plausible-looking is not the same as correct under contention or under a
deployment topology you didn't explicitly ask it to consider.

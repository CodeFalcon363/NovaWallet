# Before Implementing — Agent Operating Rules

> Work like a contractor who bills for rework: the cost of a wrong assumption is
> yours to avoid; the cost of an unnecessary question is mine to pay.

Follow these rules before writing any implementation code. They exist to catch a
wrong understanding at the cheapest possible moment.

---

## 1. Investigate before you ask

- Read the relevant code, tests, configs, and dependency manifests **first**.
- Anything discoverable in under a minute of searching is not a question — it is
  research you owe me.
- **Never ask** about:
  - test framework
  - language version
  - lint rules
  - error-handling conventions
  - directory layout
  - existing abstractions already in the repo
- If the codebase contradicts itself, that **is** worth raising.

---

## 2. Then produce this, and stop

Produce the following four sections, then **wait**. Do not begin implementing.

### Goal
One paragraph restating what I asked for in your own words, including the
acceptance criteria you will hold yourself to. If your restatement is wrong,
that is the cheapest possible place to find out.

### Blocking questions (0–3)
- Only ask when a wrong answer means **throwing work away**, not when it merely
  requires an adjustment.
- Give each question your recommended default, so I can reply "yes to all."
- Never ask an open question where a proposed answer would do.
- If nothing is genuinely blocking, say so and list zero.

### Assumptions
Numbered, specific, and falsifiable.

- Good: "Inputs are under 10k rows and fit in memory."
- Not an assumption: "The code should be maintainable."

Cover whichever of these the task actually touches:

| Area | What to pin down |
|------|------------------|
| **Data** | Shape, volume, trust level, encoding, and what a malformed input looks like. |
| **Failure** | What happens on timeout, partial write, or downstream 500 — retry, fail loudly, or degrade? |
| **Boundaries** | Who calls this; what is public API vs. internal; what backwards-compatibility obligations exist. |
| **State** | Concurrency, idempotency, transactionality, and ordering guarantees. |
| **Environment** | Runtime version, where it deploys, and what it may access or modify. |
| **Scope** | What you are deliberately not doing, and what you are leaving as TODO. |
| **Testing** | What you will write tests for, and what you will leave uncovered. |

### Plan
- List the files you will create or modify.
- Give the key function/type signatures.
- State the order in which you will work.
- Where you choose between real alternatives, name the alternative and say why
  you rejected it in one clause.

**Then wait. Do not begin implementing.**

---

## 3. Proportionality

The ceremony above scales with blast radius.

- **Just do it** — a typo fix, a rename, or a change under ~20 lines with one
  obvious correct form.
- **Full treatment, and be more suspicious than usual of your own assumptions** —
  a new module, a schema change, or anything touching auth, money, migrations,
  or deletion.

---

## 4. Prefer the simplest thing that fully solves the problem

Match the solution to the problem in front of you — not to what looks
sophisticated, and not to a problem you imagine you might have later.

- **Solve the actual case.** If a plain function, a conditional, or a few lines
  inline does the job correctly, that is the answer. Do not reach for a strategy
  pattern, an event bus, a generic base class, a pipeline, or a DI-swappable
  service when a direct call would do.
- **Earn every abstraction.** Add an interface, a layer, a config switch, a
  factory, or a generic only when there is a *second concrete caller today* or a
  *written requirement* that demands it. "It might be useful later" is not a
  reason. One implementation does not need an interface.
- **YAGNI.** Do not build extensibility, pluggability, or configurability that
  nobody asked for. Delete speculative options rather than ship them.
- **Fewer moving parts wins.** Prefer one file over three, one class over a
  class-plus-interface-plus-factory, standard library over a new dependency, a
  boring loop over a clever abstraction. Less code is less to test, break, and
  explain.
- **When you do add structure, name what forced it.** In the Plan, if a piece of
  indirection is unavoidable, state the concrete thing that requires it in one
  clause — the same way you justify rejected alternatives.
- **If you notice yourself adding a layer "to be safe" or "to be clean," stop.**
  Clean is the minimum code that is correct and readable. Raise it with me if you
  think real complexity is genuinely warranted.

This does not license sloppiness: input validation, error handling, and the
suspicion demanded by §3 for auth/money/migrations still apply in full. Simple
means *no unnecessary parts*, not *missing necessary ones*.

---

## 5. After I approve

- Implement the plan as approved.
- If you discover mid-implementation that an assumption was wrong, or the plan
  does not survive contact with the code: **stop and tell me.**
- Do not quietly improvise a different design.
- Do not press on with an approach you now believe is wrong.

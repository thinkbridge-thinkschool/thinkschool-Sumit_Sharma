# Day 9 — Task 1: Isolation Levels + Read Anomalies

All work for this task lives in `Day-9/`. No files outside `Day-9/` were
modified. Executed against the existing Day-7 Azure SQL Database
(`thinkschool-day7-sqlsrv.database.windows.net` / `day7quotesdb`), using two
genuinely concurrent connections (two OS threads, each with its own TDS
connection) — not simulated output.

## Environment note

`sqlcmd` / `mssql-cli` are not installed on this machine, so the two
concurrent sessions were driven with a small Python harness
(`azure-sql/run_concurrent_test.py`) using the `python-tds` driver. Each
"Session A" / "Session B" pair below is a real pair of connections executing
the exact SQL shown, synchronized with threading events so the interleaving
is deterministic (instead of relying on `WAITFOR DELAY` timing). The
reference SQL for each anomaly (identical to what the harness executes) is
also saved standalone in `azure-sql/dirty-read.sql`,
`azure-sql/non-repeatable-read.sql`, and `azure-sql/phantom-read.sql`.

The Day-7 database has **`READ_COMMITTED_SNAPSHOT` (RCSI) ON**, which is the
Azure SQL Database default. This matters: under RCSI, `READ COMMITTED`
readers never block on a writer's lock — they transparently read the last
committed row version instead. That's *why* `READ COMMITTED` alone is enough
to prevent a dirty read here without any blocking, whereas on a plain
on-prem SQL Server (`RCSI` off) the same prevention would show up as the
reader blocking until the writer commits. `REPEATABLE READ` and
`SERIALIZABLE` are unaffected by RCSI — they still take real locks — which
is why the blocking timestamps below show Session A's write genuinely
stalling until Session B ends its transaction.

Full raw results (including per-step timestamps in seconds since each test
started): `azure-sql/results/run_output.json`.

Test dataset (`azure-sql/schema.sql` + `azure-sql/seed.sql`, isolated
`Day9_Accounts` / `Day9_Orders` tables, re-seeded before every run):

```sql
CREATE TABLE dbo.Day9_Accounts (
    id      INT NOT NULL PRIMARY KEY,
    balance INT NOT NULL
);
-- seed: (1, 1000)

CREATE TABLE dbo.Day9_Orders (
    id          INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    customer_id INT               NOT NULL,
    amount      DECIMAL(10,2)     NOT NULL
);
-- seed: (42, 100.00), (42, 150.00), (7, 50.00), (7, 75.00)
```

Both test tables were dropped again after verification (see "Cleanup"
below); the definitions remain in `schema.sql`/`seed.sql` to reproduce them.

---

## 1. Dirty Read

**Session A** (uncommitted write, held open):
```sql
BEGIN TRANSACTION;
UPDATE dbo.Day9_Accounts SET balance = 9999 WHERE id = 1;
-- held open, uncommitted --
ROLLBACK TRANSACTION;
```

**Session B — READ UNCOMMITTED:**
```sql
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;
BEGIN TRANSACTION;
SELECT balance FROM dbo.Day9_Accounts WHERE id = 1;
COMMIT TRANSACTION;
```

### Observed result — READ UNCOMMITTED (anomaly reproduced)

| t (s) | Session | Event |
|---|---|---|
| 2.047 | A | `UPDATE balance=9999` (not yet committed) |
| 2.459 | B | `SELECT balance` → **9999** |
| 2.824 | A | `ROLLBACK` (the 9999 never actually existed) |

Session B read `9999` — a value that was rolled back and never committed.
**Dirty read reproduced.**

**Session B — READ COMMITTED:**
```sql
SET TRANSACTION ISOLATION LEVEL READ COMMITTED;
BEGIN TRANSACTION;
SELECT balance FROM dbo.Day9_Accounts WHERE id = 1;
COMMIT TRANSACTION;
```

### Observed result — READ COMMITTED (anomaly prevented)

| t (s) | Session | Event |
|---|---|---|
| 1.307 | A | `UPDATE balance=9999` (not yet committed) |
| 1.604 | B | `SELECT balance` → **1000** |
| 1.822 | A | `ROLLBACK` |

Session B read `1000` (the last *committed* value) even while A's
uncommitted write was in flight, and B's `SELECT` did not block (RCSI row
versioning). **Dirty read prevented.**

**Lowest isolation level that prevents dirty read: `READ COMMITTED`.**
(`READ UNCOMMITTED` is the only one of the four levels that permits it.)

---

## 2. Non-Repeatable Read

**Session B — READ COMMITTED:**
```sql
SET TRANSACTION ISOLATION LEVEL READ COMMITTED;
BEGIN TRANSACTION;
SELECT balance FROM dbo.Day9_Accounts WHERE id = 1;   -- read #1
-- (Session A commits its update here) --
SELECT balance FROM dbo.Day9_Accounts WHERE id = 1;   -- read #2
COMMIT TRANSACTION;
```

**Session A:**
```sql
BEGIN TRANSACTION;
UPDATE dbo.Day9_Accounts SET balance = 2000 WHERE id = 1;
COMMIT TRANSACTION;
```

### Observed result — READ COMMITTED (anomaly reproduced)

| t (s) | Session | Event |
|---|---|---|
| 1.550 | B | read #1 → **1000** |
| 1.857 | A | `UPDATE balance=2000` + `COMMIT` (completed immediately, not blocked) |
| 4.827 | B | read #2 → **2000** |
| 5.031 | B | `COMMIT` |

Two reads of the same row, in the same transaction, returned different
values. **Non-repeatable read reproduced** (RCSI means `READ COMMITTED`
takes a fresh committed snapshot on *each statement*, not once per
transaction, and does not block the writer either).

**Session B — REPEATABLE READ:**
```sql
SET TRANSACTION ISOLATION LEVEL REPEATABLE READ;
BEGIN TRANSACTION;
SELECT balance FROM dbo.Day9_Accounts WHERE id = 1;   -- read #1
-- (Session A's UPDATE now blocks) --
SELECT balance FROM dbo.Day9_Accounts WHERE id = 1;   -- read #2
COMMIT TRANSACTION;
```

### Observed result — REPEATABLE READ (anomaly prevented)

| t (s) | Session | Event |
|---|---|---|
| 2.040 | B | read #1 → **1000** |
| 2.041 | A | attempts `UPDATE balance=2000` — **blocks** on B's shared lock |
| 5.320 | B | read #2 → **1000** (unchanged) |
| 5.550 | B | `COMMIT` |
| 5.624 | A | `UPDATE` finally completes + `COMMIT` (0.07s after B released its lock) |

Session A's write attempt sat blocked for ~3.58s until Session B's
transaction ended — B's two reads inside its own transaction stayed
identical. **Non-repeatable read prevented.**

**Lowest isolation level that prevents non-repeatable read:
`REPEATABLE READ`.**

---

## 3. Phantom Read

**Session B — REPEATABLE READ:**
```sql
SET TRANSACTION ISOLATION LEVEL REPEATABLE READ;
BEGIN TRANSACTION;
SELECT COUNT(*) FROM dbo.Day9_Orders WHERE customer_id = 42;   -- read #1
-- (Session A inserts a new matching row here) --
SELECT COUNT(*) FROM dbo.Day9_Orders WHERE customer_id = 42;   -- read #2
COMMIT TRANSACTION;
```

**Session A:**
```sql
BEGIN TRANSACTION;
INSERT INTO dbo.Day9_Orders (customer_id, amount) VALUES (42, 200.00);
COMMIT TRANSACTION;
```

### Observed result — REPEATABLE READ (anomaly reproduced)

| t (s) | Session | Event |
|---|---|---|
| 2.025 | B | read #1 count → **2** |
| 2.332 | A | `INSERT customer_id=42` + `COMMIT` (completed immediately, not blocked) |
| 5.302 | B | read #2 count → **3** |
| 5.507 | B | `COMMIT` |

`REPEATABLE READ` locks the rows Session B already read, but not the
"gap"/range, so Session A's insert went through unblocked and a new
matching row appeared on B's second read. **Phantom read reproduced.**

**Session B — SERIALIZABLE:**
```sql
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
BEGIN TRANSACTION;
SELECT COUNT(*) FROM dbo.Day9_Orders WHERE customer_id = 42;   -- read #1
-- (Session A's INSERT now blocks) --
SELECT COUNT(*) FROM dbo.Day9_Orders WHERE customer_id = 42;   -- read #2
COMMIT TRANSACTION;
```

### Observed result — SERIALIZABLE (anomaly prevented)

| t (s) | Session | Event |
|---|---|---|
| 1.433 | B | read #1 count → **2** |
| 1.434 | A | attempts `INSERT customer_id=42` — **blocks** on B's key-range lock |
| 4.796 | B | read #2 count → **2** (unchanged) |
| 5.120 | B | `COMMIT` |
| 5.206 | A | `INSERT` finally completes + `COMMIT` (0.09s after B released its lock) |

Session A's insert sat blocked for ~3.77s until Session B's transaction
ended. `SERIALIZABLE` took a key-range lock over `customer_id = 42`, not
just the two existing rows, so no phantom could be inserted while B's
transaction was open. **Phantom read prevented.**

**Lowest isolation level that prevents phantom read: `SERIALIZABLE`.**
(`REPEATABLE READ` is not sufficient — it protects only rows already read,
not the range/predicate.)

---

## Summary

| Anomaly | Lowest isolation level that prevents it |
|---|---|
| Dirty read | READ COMMITTED |
| Non-repeatable read | REPEATABLE READ |
| Phantom read | SERIALIZABLE |

## Files

```
Day-9/
  README.md
  azure-sql/
    schema.sql                 -- Day9_Accounts / Day9_Orders table definitions
    seed.sql                   -- deterministic seed data, re-run before each test
    dirty-read.sql             -- reference Session A/B SQL for the dirty-read test
    non-repeatable-read.sql    -- reference Session A/B SQL for the non-repeatable-read test
    phantom-read.sql           -- reference Session A/B SQL for the phantom-read test
    run_concurrent_test.py     -- two-thread harness that actually ran both sessions concurrently
    results/
      run_output.json          -- full captured timestamps/values for every run above
```

## Cleanup

`Day9_Accounts` and `Day9_Orders` were dropped from `day7quotesdb` after all
six runs above were captured (`run_concurrent_test.py cleanup`). Re-run
`schema.sql` + `seed.sql` to recreate the deterministic dataset if these
tests need to be re-verified.

Two new server-level firewall rules were added to `thinkschool-day7-sqlsrv`
to let this dev machine reach the database this session (its current
network path is IPv6/NAT64-only, unlike when the server was first created):
`AllowMyClientIPv6` (IPv6 firewall rule) and `AllowMyClientIPv4NAT64` (the
IPv4 address the NAT64 gateway presents to Azure). No existing rules were
removed.

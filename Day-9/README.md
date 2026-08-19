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

---

# Day 9 — Task 2: Reproduce and Resolve a Deadlock

All work for this task also lives in `Day-9/` (`azure-sql/deadlock-*.sql`,
`azure-sql/deadlock_test.py`, `azure-sql/results/deadlock_*`). No files
outside `Day-9/` were modified. Executed against the same Day-7 Azure SQL
Database (`thinkschool-day7-sqlsrv.database.windows.net` / `day7quotesdb`),
reusing the credential and network setup from Task 1 (Azure SQL password
never re-generated, never committed — read from the local scratchpad file
used in Task 1 and passed via the `DAY9_SQL_PASSWORD` environment
variable). `sqlcmd`/`mssql-cli` are still not installed on this machine, so
the two concurrent sessions were driven the same way as Task 1: a small
Python harness (`azure-sql/deadlock_test.py`, `python-tds` driver) opening
**two separate TDS connections on two OS threads**, synchronized with
`threading.Event`s so each session provably holds its first lock before the
other attempts its second update — a real, deterministic interleaving, not
`WAITFOR DELAY` timing and not a simulated result.

## Deadlock scenario

Two tables, one row each:

```sql
CREATE TABLE dbo.Day9_Resource1 (id INT NOT NULL PRIMARY KEY, value INT NOT NULL);
CREATE TABLE dbo.Day9_Resource2 (id INT NOT NULL PRIMARY KEY, value INT NOT NULL);
-- seed: Resource1(1, 100), Resource2(1, 200)
```
(`azure-sql/deadlock-schema.sql`, `azure-sql/deadlock-seed.sql`; re-seeded
before every run.)

Classic two-resource circular wait:
- **Session A** locks `Resource1`, then attempts `Resource2`.
- **Session B** locks `Resource2`, then attempts `Resource1`.

Both sessions were synchronized so that each one's *first* `UPDATE` had
genuinely executed (i.e. the row lock was actually held) before either
attempted its *second* `UPDATE` — guaranteeing a real circular wait rather
than a lucky race.

### Session A — broken (`azure-sql/deadlock-reproduce.sql`)

```sql
BEGIN TRANSACTION;
UPDATE dbo.Day9_Resource1 SET value = value + 1 WHERE id = 1;   -- locks Resource1
-- (wait here until Session B has locked Resource2)
UPDATE dbo.Day9_Resource2 SET value = value + 1 WHERE id = 1;   -- blocks on Resource2, held by B
COMMIT TRANSACTION;
```

### Session B — broken (`azure-sql/deadlock-reproduce.sql`)

```sql
BEGIN TRANSACTION;
UPDATE dbo.Day9_Resource2 SET value = value + 1 WHERE id = 1;   -- locks Resource2
-- (wait here until Session A has locked Resource1)
UPDATE dbo.Day9_Resource1 SET value = value + 1 WHERE id = 1;   -- blocks on Resource1, held by A
COMMIT TRANSACTION;
```

## Actual deadlock result (real execution, not simulated)

Run via `DAY9_SQL_PASSWORD=*** python deadlock_test.py deadlock`. Timestamps
are seconds since the run started; full raw log in
`azure-sql/results/deadlock_result.json`.

| t (s) | Session | Event |
|---|---|---|
| 1.734 | A | locked Resource1 |
| 1.734 | B | locked Resource2 |
| 1.734 | A | attempts `UPDATE Resource2` — blocks (held by B) |
| 1.734 | B | attempts `UPDATE Resource1` — blocks (held by A) |
| 1.957 | A | **error**: `Transaction (Process ID 94) was deadlocked on lock resources with another process and has been chosen as the deadlock victim. Rerun the transaction.` |
| 2.007 | B | `COMMIT` — B's transaction completed normally |

SQL Server error **1205**, raised on Session A's connection (SPID 94), ~0.22s
after both sessions became mutually blocked — consistent with SQL Server's
deadlock monitor waking on its detection interval and finding the cycle.
Session B (SPID 89) was never interrupted and committed normally.

This was run twice (once before the Extended Events capture session existed,
once after) and both times SQL Server detected the deadlock and picked a
victim — it is not a one-off race, it is the guaranteed outcome of this lock
ordering under real concurrency.

### Why the deadlock happened

Session A held a lock on `Resource1` and wanted `Resource2`; Session B held
a lock on `Resource2` and wanted `Resource1`. Neither could proceed without
the other releasing its lock first, and neither would release its lock
before committing — a circular wait (A → B → A) with no way out except for
one transaction to be forcibly rolled back. SQL Server's lock monitor
detects this cycle and kills one participant (the "deadlock victim") to
break it, raising error 1205 on that connection while letting the other
proceed.

### Which session became the victim, and why

**Session A (SPID 94) was the victim.** SQL Server's victim selection is
primarily driven by `DEADLOCK_PRIORITY` (equal/default for both sessions
here) and, as a tiebreaker, the estimated cost to roll back each
transaction — the transaction that is cheaper to undo is chosen. Both
transactions here had done a single one-row `UPDATE`, so the two were
essentially tied on rollback cost; which specific session loses in that case
comes down to low-level scheduling (e.g. which process the deadlock monitor
happens to enumerate/evaluate first), not anything meaningfully different
about A's or B's SQL. The deadlock graph below shows A's transaction
(`xactid="13287"`) waiting in **S** mode for the key lock B holds in **X**
mode, and vice versa for B (`xactid="13283"`) — a symmetric cycle, confirming
victim choice wasn't driven by lock mode or resource asymmetry.

## Deadlock graph evidence

Azure SQL Database does **not** expose the on-prem `sqlserver.xml_deadlock_report`
Extended Event, and this database has no pre-started `system_health`
database-scoped session (`sys.dm_xe_database_sessions` returned nothing
before this task). The Azure SQL-supported equivalent is
**`sqlserver.database_xml_deadlock_report`**, discovered via:

```sql
SELECT p.name, o.name, o.description
FROM sys.dm_xe_objects o
JOIN sys.dm_xe_packages p ON o.package_guid = p.guid
WHERE o.name LIKE '%deadlock%';
```

A database-scoped Extended Events session was created and started for this
task (`deadlock_test.py create_xe_session()` / `xe-start` command):

```sql
CREATE EVENT SESSION day9_deadlock_capture ON DATABASE
ADD EVENT sqlserver.database_xml_deadlock_report
ADD TARGET package0.ring_buffer
WITH (MAX_MEMORY=4MB, EVENT_RETENTION_MODE=ALLOW_SINGLE_EVENT_LOSS, MAX_DISPATCH_LATENCY=5SECONDS);

ALTER EVENT SESSION day9_deadlock_capture ON DATABASE STATE = START;
```

...and read back after the deadlock ran (`deadlock_test.py graph` command):

```sql
SELECT CAST(xet.target_data AS NVARCHAR(MAX))
FROM sys.dm_xe_database_session_targets xet
JOIN sys.dm_xe_database_sessions xe ON xet.event_session_address = xe.address
WHERE xe.name = 'day9_deadlock_capture' AND xet.target_name = 'ring_buffer';
```

Full raw ring-buffer XML: `azure-sql/results/deadlock_ring_buffer.xml`.
Extracted `<deadlock>...</deadlock>` report (raw): `azure-sql/results/deadlock_graph.xml`.
Trimmed for readability (native call-stack frames collapsed, everything else
untouched): `azure-sql/results/deadlock_graph_trimmed.xml`. Key excerpt:

```xml
<deadlock>
 <victim-list>
  <victimProcess id="process2457f6d6478"/>
 </victim-list>
 <process-list>
  <process id="process2457f6d6478" ... spid="94" ... xactid="13287"
            lockMode="S" clientapp="pytds" loginname="day7admin" ...>
   <inputbuf>
UPDATE dbo.Day9_Resource2 SET value = value + 1 WHERE id = 1   </inputbuf>
  </process>
  <process id="process2456a146868" ... spid="89" ... xactid="13283"
            lockMode="S" clientapp="pytds" loginname="day7admin" ...>
   <inputbuf>
UPDATE dbo.Day9_Resource1 SET value = value + 1 WHERE id = 1   </inputbuf>
  </process>
 </process-list>
 <resource-list>
  <xactlock ... objectname="...dbo.Day9_Resource2" ...>
   <owner-list><owner id="process2456a146868" mode="X"/></owner-list>   <!-- B owns Resource2 -->
   <waiter-list><waiter id="process2457f6d6478" mode="S"/></waiter-list> <!-- A waits on Resource2 -->
  </xactlock>
  <xactlock ... objectname="...dbo.Day9_Resource1" ...>
   <owner-list><owner id="process2457f6d6478" mode="X"/></owner-list>   <!-- A owns Resource1 -->
   <waiter-list><waiter id="process2456a146868" mode="S"/></waiter-list> <!-- B waits on Resource1 -->
  </xactlock>
 </resource-list>
</deadlock>
```

`victimProcess id="process2457f6d6478"` is spid 94 — Session A — matching
exactly the `Process ID 94` named in the error message the Python harness
caught, and the two `xactlock` entries show the textbook cycle: B owns
Resource2 exclusively while A waits on it, and A owns Resource1 exclusively
while B waits on it.

## Fixed version — consistent lock ordering

Both sessions now acquire `Resource1` **before** `Resource2` — the same
order for both.

### Session A — fixed (`azure-sql/deadlock-fixed.sql`)

```sql
BEGIN TRANSACTION;
UPDATE dbo.Day9_Resource1 SET value = value + 1 WHERE id = 1;   -- locks Resource1
-- (wait here — same synchronization point as the broken version)
UPDATE dbo.Day9_Resource2 SET value = value + 1 WHERE id = 1;   -- Resource2 is free, proceeds
COMMIT TRANSACTION;
```

### Session B — fixed (`azure-sql/deadlock-fixed.sql`)

```sql
BEGIN TRANSACTION;
UPDATE dbo.Day9_Resource1 SET value = value + 1 WHERE id = 1;   -- blocks until A releases Resource1
UPDATE dbo.Day9_Resource2 SET value = value + 1 WHERE id = 1;   -- Resource2 now free
COMMIT TRANSACTION;
```

### Actual result proving the fix works (real execution)

Run via `DAY9_SQL_PASSWORD=*** python deadlock_test.py fixed`. Full raw log
in `azure-sql/results/fixed_result.json`.

| t (s) | Session | Event |
|---|---|---|
| 1.361 | A | locked Resource1 |
| 1.361 | B | attempts `UPDATE Resource1` — **blocks** (held by A), no error |
| 3.131 | A | locks Resource2 + `COMMIT` (releases Resource1) |
| 3.131 | B | Resource1 lock now granted — unblocked |
| 3.298 | B | locks Resource2 + `COMMIT` |

Result: `{"a_result": "committed", "b_result": "committed",
"both_committed_no_deadlock": true}`. Session B genuinely blocked for
~1.77s waiting its turn for `Resource1` (real lock contention still exists —
that's expected and correct), then proceeded and committed once Session A's
transaction ended. **Neither session errored; no deadlock (error 1205)
occurred.** The `day9_deadlock_capture` Extended Events session was checked
again after this run and still contained exactly the one deadlock report
from the broken run above — zero new deadlock events were recorded for the
fixed version.

### Why consistent lock ordering fixes it

If every transaction acquires the shared resources in the same fixed order,
a circular wait becomes impossible — the second transaction can only ever be
waiting on the first (never the reverse), so at worst one session queues
behind the other instead of both being mutually blocked.

## Cleanup

`Day9_Resource1`, `Day9_Resource2`, and the `day9_deadlock_capture` Extended
Events session were all dropped after verification
(`deadlock_test.py cleanup`), confirmed empty by querying
`sys.tables`/`sys.database_event_sessions` for `Day9_%`/`day9%` afterwards.

## Files

```
Day-9/
  azure-sql/
    deadlock-schema.sql          -- Day9_Resource1 / Day9_Resource2 table definitions
    deadlock-seed.sql            -- deterministic seed data, re-run before each run
    deadlock-reproduce.sql       -- reference Session A/B SQL for the broken (deadlocking) version
    deadlock-fixed.sql           -- reference Session A/B SQL for the fixed (consistent lock ordering) version
    deadlock_test.py             -- two-thread harness: runs broken/fixed versions, manages the XE capture session, fetches the deadlock graph
    results/
      deadlock_result.json       -- captured timestamps/outcome for the broken run
      fixed_result.json          -- captured timestamps/outcome for the fixed run
      deadlock_ring_buffer.xml   -- full raw Extended Events ring-buffer target data
      deadlock_graph.xml         -- extracted <deadlock>...</deadlock> report (raw)
      deadlock_graph_trimmed.xml -- same report with native call-stack frames collapsed for readability
```


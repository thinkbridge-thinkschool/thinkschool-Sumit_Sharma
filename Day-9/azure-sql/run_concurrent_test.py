"""
Day-9 Task 1: drives the dirty-read / non-repeatable-read / phantom-read
experiments as two genuinely concurrent connections against Azure SQL
(thinkschool-day7-sqlsrv / day7quotesdb), using two threads each with its
own connection so Session A and Session B run truly in parallel.

Requires: pip install python-tds "pyopenssl==22.1.0"
Credentials are never hardcoded: set DAY9_SQL_PASSWORD in the environment
before running. Nothing here is committed with a real password.

Usage:
    DAY9_SQL_PASSWORD=*** python run_concurrent_test.py all
"""
import json
import os
import sys
import threading
import time

import pytds

SERVER = "thinkschool-day7-sqlsrv.database.windows.net"
USER = "day7admin"
DATABASE = "day7quotesdb"
CAFILE = "/etc/ssl/certs/ca-certificates.crt"


def connect():
    password = os.environ["DAY9_SQL_PASSWORD"]
    return pytds.connect(
        server=SERVER,
        port=1433,
        user=USER,
        password=password,
        database=DATABASE,
        timeout=30,
        login_timeout=20,
        cafile=CAFILE,
        validate_host=True,
        autocommit=True,  # we issue explicit BEGIN/COMMIT/ROLLBACK TRANSACTION ourselves
    )


def reseed():
    conn = connect()
    cur = conn.cursor()
    with open(os.path.join(os.path.dirname(__file__), "seed.sql")) as f:
        seed_sql = f.read()
    for stmt in seed_sql.split(";"):
        stmt = stmt.strip()
        if stmt:
            cur.execute(stmt)
    conn.close()


def now():
    return round(time.perf_counter(), 3)


# ---------------------------------------------------------------------------
# Dirty read
# ---------------------------------------------------------------------------
def dirty_read_test(isolation_level):
    reseed()
    log = []
    t0 = now()

    a_conn = connect()
    b_conn = connect()
    a_cur = a_conn.cursor()
    b_cur = b_conn.cursor()

    a_updated = threading.Event()
    b_done = threading.Event()

    def session_a():
        a_cur.execute("BEGIN TRANSACTION")
        a_cur.execute("UPDATE dbo.Day9_Accounts SET balance = 9999 WHERE id = 1")
        log.append({"t": now() - t0, "session": "A", "step": "UPDATE balance=9999 (uncommitted)"})
        a_updated.set()
        b_done.wait(timeout=15)
        a_cur.execute("ROLLBACK TRANSACTION")
        log.append({"t": now() - t0, "session": "A", "step": "ROLLBACK"})

    def session_b():
        a_updated.wait(timeout=15)
        b_cur.execute(f"SET TRANSACTION ISOLATION LEVEL {isolation_level}")
        b_cur.execute("BEGIN TRANSACTION")
        b_cur.execute("SELECT balance FROM dbo.Day9_Accounts WHERE id = 1")
        row = b_cur.fetchone()
        log.append({"t": now() - t0, "session": "B", "step": f"SELECT balance -> {row[0]}", "value": row[0]})
        b_cur.execute("COMMIT TRANSACTION")
        b_done.set()

    ta = threading.Thread(target=session_a)
    tb = threading.Thread(target=session_b)
    ta.start(); tb.start()
    ta.join(timeout=20); tb.join(timeout=20)

    a_conn.close(); b_conn.close()

    b_value = next(e["value"] for e in log if e["session"] == "B" and "value" in e)
    anomaly_observed = (b_value == 9999)
    return {
        "scenario": "dirty_read",
        "session_b_isolation": isolation_level,
        "log": log,
        "session_b_observed_balance": b_value,
        "committed_balance_before_and_after": 1000,
        "anomaly_observed": anomaly_observed,
    }


# ---------------------------------------------------------------------------
# Non-repeatable read
# ---------------------------------------------------------------------------
def non_repeatable_read_test(isolation_level):
    reseed()
    log = []
    t0 = now()

    a_conn = connect()
    b_conn = connect()
    a_cur = a_conn.cursor()
    b_cur = b_conn.cursor()

    b_read1_done = threading.Event()
    b_finished = threading.Event()

    a_timing = {}

    def session_a():
        b_read1_done.wait(timeout=15)
        a_timing["update_attempt_start"] = now() - t0
        a_cur.execute("BEGIN TRANSACTION")
        a_cur.execute("UPDATE dbo.Day9_Accounts SET balance = 2000 WHERE id = 1")
        a_cur.execute("COMMIT TRANSACTION")
        a_timing["update_committed"] = now() - t0
        log.append({"t": a_timing["update_committed"], "session": "A", "step": "UPDATE balance=2000 + COMMIT"})

    def session_b():
        b_cur.execute(f"SET TRANSACTION ISOLATION LEVEL {isolation_level}")
        b_cur.execute("BEGIN TRANSACTION")
        b_cur.execute("SELECT balance FROM dbo.Day9_Accounts WHERE id = 1")
        read1 = b_cur.fetchone()[0]
        log.append({"t": now() - t0, "session": "B", "step": f"read #1 -> {read1}", "value": read1})
        b_read1_done.set()
        # Give Session A a real window to attempt the write (and, under
        # REPEATABLE READ, to actually block on B's shared lock).
        time.sleep(3)
        b_cur.execute("SELECT balance FROM dbo.Day9_Accounts WHERE id = 1")
        read2 = b_cur.fetchone()[0]
        log.append({"t": now() - t0, "session": "B", "step": f"read #2 -> {read2}", "value": read2})
        b_cur.execute("COMMIT TRANSACTION")
        log.append({"t": now() - t0, "session": "B", "step": "COMMIT"})
        b_finished.set()

    ta = threading.Thread(target=session_a)
    tb = threading.Thread(target=session_b)
    tb.start(); ta.start()
    tb.join(timeout=25); ta.join(timeout=25)

    a_conn.close(); b_conn.close()

    reads = [e["value"] for e in log if e["session"] == "B" and "value" in e]
    read1, read2 = reads[0], reads[1]
    anomaly_observed = (read1 != read2)
    return {
        "scenario": "non_repeatable_read",
        "session_b_isolation": isolation_level,
        "log": log,
        "a_timing": a_timing,
        "session_b_read1": read1,
        "session_b_read2": read2,
        "anomaly_observed": anomaly_observed,
    }


# ---------------------------------------------------------------------------
# Phantom read
# ---------------------------------------------------------------------------
def phantom_read_test(isolation_level):
    reseed()
    log = []
    t0 = now()

    a_conn = connect()
    b_conn = connect()
    a_cur = a_conn.cursor()
    b_cur = b_conn.cursor()

    b_read1_done = threading.Event()
    b_finished = threading.Event()
    a_timing = {}

    def session_a():
        b_read1_done.wait(timeout=15)
        a_timing["insert_attempt_start"] = now() - t0
        a_cur.execute("BEGIN TRANSACTION")
        a_cur.execute("INSERT INTO dbo.Day9_Orders (customer_id, amount) VALUES (42, 200.00)")
        a_cur.execute("COMMIT TRANSACTION")
        a_timing["insert_committed"] = now() - t0
        log.append({"t": a_timing["insert_committed"], "session": "A", "step": "INSERT customer_id=42 + COMMIT"})

    def session_b():
        b_cur.execute(f"SET TRANSACTION ISOLATION LEVEL {isolation_level}")
        b_cur.execute("BEGIN TRANSACTION")
        b_cur.execute("SELECT COUNT(*) FROM dbo.Day9_Orders WHERE customer_id = 42")
        read1 = b_cur.fetchone()[0]
        log.append({"t": now() - t0, "session": "B", "step": f"read #1 count -> {read1}", "value": read1})
        b_read1_done.set()
        time.sleep(3)
        b_cur.execute("SELECT COUNT(*) FROM dbo.Day9_Orders WHERE customer_id = 42")
        read2 = b_cur.fetchone()[0]
        log.append({"t": now() - t0, "session": "B", "step": f"read #2 count -> {read2}", "value": read2})
        b_cur.execute("COMMIT TRANSACTION")
        log.append({"t": now() - t0, "session": "B", "step": "COMMIT"})
        b_finished.set()

    ta = threading.Thread(target=session_a)
    tb = threading.Thread(target=session_b)
    tb.start(); ta.start()
    tb.join(timeout=25); ta.join(timeout=25)

    a_conn.close(); b_conn.close()

    reads = [e["value"] for e in log if e["session"] == "B" and "value" in e]
    read1, read2 = reads[0], reads[1]
    anomaly_observed = (read1 != read2)
    return {
        "scenario": "phantom_read",
        "session_b_isolation": isolation_level,
        "log": log,
        "a_timing": a_timing,
        "session_b_read1_count": read1,
        "session_b_read2_count": read2,
        "anomaly_observed": anomaly_observed,
    }


def cleanup():
    conn = connect()
    cur = conn.cursor()
    cur.execute("IF OBJECT_ID('dbo.Day9_Accounts', 'U') IS NOT NULL DROP TABLE dbo.Day9_Accounts")
    cur.execute("IF OBJECT_ID('dbo.Day9_Orders', 'U') IS NOT NULL DROP TABLE dbo.Day9_Orders")
    conn.close()


if __name__ == "__main__":
    cmd = sys.argv[1] if len(sys.argv) > 1 else "all"
    results_dir = os.path.join(os.path.dirname(__file__), "results")
    os.makedirs(results_dir, exist_ok=True)

    if cmd == "cleanup":
        cleanup()
        print("cleanup done")
        sys.exit(0)

    results = {}
    results["dirty_read_uncommitted"] = dirty_read_test("READ UNCOMMITTED")
    results["dirty_read_committed"] = dirty_read_test("READ COMMITTED")
    results["non_repeatable_read_committed"] = non_repeatable_read_test("READ COMMITTED")
    results["non_repeatable_read_repeatable"] = non_repeatable_read_test("REPEATABLE READ")
    results["phantom_read_repeatable"] = phantom_read_test("REPEATABLE READ")
    results["phantom_read_serializable"] = phantom_read_test("SERIALIZABLE")

    out_path = os.path.join(results_dir, "run_output.json")
    with open(out_path, "w") as f:
        json.dump(results, f, indent=2)

    for name, r in results.items():
        print(f"{name}: anomaly_observed={r['anomaly_observed']}")
    print(f"Full results written to {out_path}")

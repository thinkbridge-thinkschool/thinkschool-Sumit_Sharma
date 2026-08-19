"""
Day-9 Task 2: reproduces a genuine classic two-resource deadlock against
Azure SQL (thinkschool-day7-sqlsrv / day7quotesdb) using two real concurrent
connections (two OS threads, each with its own TDS connection), then re-runs
a fixed version with consistent lock ordering to prove the deadlock is gone.

Requires: pip install python-tds "pyopenssl==22.1.0"  (already installed in
the scratchpad venv reused from Day-9 Task 1).

Credentials are never hardcoded: set DAY9_SQL_PASSWORD in the environment
before running. Nothing here is committed with a real password.

Usage:
    DAY9_SQL_PASSWORD=*** python deadlock_test.py deadlock   # broken version
    DAY9_SQL_PASSWORD=*** python deadlock_test.py fixed      # fixed version
    DAY9_SQL_PASSWORD=*** python deadlock_test.py graph      # fetch deadlock graph evidence
    DAY9_SQL_PASSWORD=*** python deadlock_test.py cleanup    # drop test tables
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
HERE = os.path.dirname(__file__)
RESULTS_DIR = os.path.join(HERE, "results")


def connect():
    password = os.environ["DAY9_SQL_PASSWORD"]
    return pytds.connect(
        server=SERVER,
        port=1433,
        user=USER,
        password=password,
        database=DATABASE,
        timeout=30,
        login_timeout=25,
        cafile=CAFILE,
        validate_host=True,
        autocommit=True,  # explicit BEGIN/COMMIT/ROLLBACK TRANSACTION ourselves
    )


def run_sql_file(path):
    conn = connect()
    cur = conn.cursor()
    with open(path) as f:
        sql = f.read()
    for stmt in sql.split(";"):
        stmt = stmt.strip()
        if stmt:
            cur.execute(stmt)
    conn.close()


def reseed():
    run_sql_file(os.path.join(HERE, "deadlock-schema.sql"))
    run_sql_file(os.path.join(HERE, "deadlock-seed.sql"))


def now():
    return round(time.perf_counter(), 3)


# ---------------------------------------------------------------------------
# Broken version: A locks R1->R2, B locks R2->R1 => classic deadlock
# ---------------------------------------------------------------------------
def deadlock_test():
    reseed()
    log = []
    t0 = now()

    a_conn = connect()
    b_conn = connect()
    a_cur = a_conn.cursor()
    b_cur = b_conn.cursor()

    a_locked_r1 = threading.Event()
    b_locked_r2 = threading.Event()

    outcome = {}

    def session_a():
        try:
            a_cur.execute("SELECT @@SPID")
            outcome["a_spid"] = a_cur.fetchone()[0]
            a_cur.execute("BEGIN TRANSACTION")
            a_cur.execute("UPDATE dbo.Day9_Resource1 SET value = value + 1 WHERE id = 1")
            log.append({"t": now() - t0, "session": "A", "step": "locked Resource1"})
            a_locked_r1.set()
            b_locked_r2.wait(timeout=15)
            log.append({"t": now() - t0, "session": "A", "step": "attempting Resource2 (will block)"})
            a_cur.execute("UPDATE dbo.Day9_Resource2 SET value = value + 1 WHERE id = 1")
            a_cur.execute("COMMIT TRANSACTION")
            log.append({"t": now() - t0, "session": "A", "step": "COMMIT (A won, not the victim)"})
            outcome["a_result"] = "committed"
        except Exception as e:
            log.append({"t": now() - t0, "session": "A", "step": f"ERROR: {e}"})
            outcome["a_result"] = "error"
            outcome["a_error"] = str(e)

    def session_b():
        try:
            b_cur.execute("SELECT @@SPID")
            outcome["b_spid"] = b_cur.fetchone()[0]
            b_cur.execute("BEGIN TRANSACTION")
            b_cur.execute("UPDATE dbo.Day9_Resource2 SET value = value + 1 WHERE id = 1")
            log.append({"t": now() - t0, "session": "B", "step": "locked Resource2"})
            b_locked_r2.set()
            a_locked_r1.wait(timeout=15)
            log.append({"t": now() - t0, "session": "B", "step": "attempting Resource1 (will block)"})
            b_cur.execute("UPDATE dbo.Day9_Resource1 SET value = value + 1 WHERE id = 1")
            b_cur.execute("COMMIT TRANSACTION")
            log.append({"t": now() - t0, "session": "B", "step": "COMMIT (B won, not the victim)"})
            outcome["b_result"] = "committed"
        except Exception as e:
            log.append({"t": now() - t0, "session": "B", "step": f"ERROR: {e}"})
            outcome["b_result"] = "error"
            outcome["b_error"] = str(e)

    ta = threading.Thread(target=session_a)
    tb = threading.Thread(target=session_b)
    ta.start(); tb.start()
    ta.join(timeout=30); tb.join(timeout=30)

    try:
        a_conn.close()
    except Exception:
        pass
    try:
        b_conn.close()
    except Exception:
        pass

    log.sort(key=lambda e: e["t"])
    deadlock_detected = (outcome.get("a_result") == "error") != (outcome.get("b_result") == "error")
    victim = "A" if outcome.get("a_result") == "error" else ("B" if outcome.get("b_result") == "error" else None)

    return {
        "scenario": "deadlock_broken_lock_ordering",
        "log": log,
        "outcome": outcome,
        "deadlock_detected": deadlock_detected,
        "victim_session": victim,
    }


# ---------------------------------------------------------------------------
# Fixed version: both sessions lock R1 then R2, same order => no deadlock
# ---------------------------------------------------------------------------
def fixed_test():
    reseed()
    log = []
    t0 = now()

    a_conn = connect()
    b_conn = connect()
    a_cur = a_conn.cursor()
    b_cur = b_conn.cursor()

    a_locked_r1 = threading.Event()
    b_attempted_r1 = threading.Event()

    outcome = {}

    def session_a():
        try:
            a_cur.execute("BEGIN TRANSACTION")
            a_cur.execute("UPDATE dbo.Day9_Resource1 SET value = value + 1 WHERE id = 1")
            log.append({"t": now() - t0, "session": "A", "step": "locked Resource1"})
            a_locked_r1.set()
            # give B a real window to attempt Resource1 and genuinely block on it
            b_attempted_r1.wait(timeout=10)
            time.sleep(1.5)
            a_cur.execute("UPDATE dbo.Day9_Resource2 SET value = value + 1 WHERE id = 1")
            a_cur.execute("COMMIT TRANSACTION")
            log.append({"t": now() - t0, "session": "A", "step": "locked Resource2 + COMMIT (releases Resource1)"})
            outcome["a_result"] = "committed"
        except Exception as e:
            log.append({"t": now() - t0, "session": "A", "step": f"ERROR: {e}"})
            outcome["a_result"] = "error"
            outcome["a_error"] = str(e)

    def session_b():
        try:
            a_locked_r1.wait(timeout=10)
            log.append({"t": now() - t0, "session": "B", "step": "attempting Resource1 (will block on A)"})
            b_cur.execute("BEGIN TRANSACTION")
            b_attempted_r1.set()
            b_cur.execute("UPDATE dbo.Day9_Resource1 SET value = value + 1 WHERE id = 1")
            log.append({"t": now() - t0, "session": "B", "step": "locked Resource1 (unblocked after A committed)"})
            b_cur.execute("UPDATE dbo.Day9_Resource2 SET value = value + 1 WHERE id = 1")
            b_cur.execute("COMMIT TRANSACTION")
            log.append({"t": now() - t0, "session": "B", "step": "locked Resource2 + COMMIT"})
            outcome["b_result"] = "committed"
        except Exception as e:
            log.append({"t": now() - t0, "session": "B", "step": f"ERROR: {e}"})
            outcome["b_result"] = "error"
            outcome["b_error"] = str(e)

    ta = threading.Thread(target=session_a)
    tb = threading.Thread(target=session_b)
    ta.start(); tb.start()
    ta.join(timeout=30); tb.join(timeout=30)

    try:
        a_conn.close()
    except Exception:
        pass
    try:
        b_conn.close()
    except Exception:
        pass

    log.sort(key=lambda e: e["t"])
    both_committed = outcome.get("a_result") == "committed" and outcome.get("b_result") == "committed"

    return {
        "scenario": "fixed_consistent_lock_ordering",
        "log": log,
        "outcome": outcome,
        "both_committed_no_deadlock": both_committed,
    }


# ---------------------------------------------------------------------------
# Deadlock graph evidence.
#
# Azure SQL Database does not expose the on-prem `sqlserver.xml_deadlock_report`
# XEvent or a pre-started `system_health` database-scoped session on this
# database. The Azure-supported equivalent is `sqlserver.database_xml_deadlock_report`,
# captured via a database-scoped Extended Events session we create ourselves,
# read back through the sys.dm_xe_database_session_targets ring-buffer DMV.
# ---------------------------------------------------------------------------
XE_SESSION_NAME = "day9_deadlock_capture"


def create_xe_session():
    conn = connect()
    cur = conn.cursor()
    cur.execute(
        f"IF EXISTS (SELECT 1 FROM sys.database_event_sessions WHERE name = '{XE_SESSION_NAME}') "
        f"DROP EVENT SESSION {XE_SESSION_NAME} ON DATABASE"
    )
    cur.execute(
        f"""
        CREATE EVENT SESSION {XE_SESSION_NAME} ON DATABASE
        ADD EVENT sqlserver.database_xml_deadlock_report
        ADD TARGET package0.ring_buffer
        WITH (MAX_MEMORY=4MB, EVENT_RETENTION_MODE=ALLOW_SINGLE_EVENT_LOSS, MAX_DISPATCH_LATENCY=5SECONDS)
        """
    )
    cur.execute(f"ALTER EVENT SESSION {XE_SESSION_NAME} ON DATABASE STATE = START")
    conn.close()


def drop_xe_session():
    conn = connect()
    cur = conn.cursor()
    cur.execute(
        f"IF EXISTS (SELECT 1 FROM sys.database_event_sessions WHERE name = '{XE_SESSION_NAME}') "
        f"DROP EVENT SESSION {XE_SESSION_NAME} ON DATABASE"
    )
    conn.close()


def fetch_deadlock_graph():
    conn = connect()
    cur = conn.cursor()
    cur.execute(
        f"""
        SELECT CAST(xet.target_data AS NVARCHAR(MAX)) AS target_data
        FROM sys.dm_xe_database_session_targets xet
        JOIN sys.dm_xe_database_sessions xe
          ON xet.event_session_address = xe.address
        WHERE xe.name = '{XE_SESSION_NAME}'
          AND xet.target_name = 'ring_buffer'
        """
    )
    row = cur.fetchone()
    conn.close()
    if row is None:
        return None
    return row[0]


def extract_deadlock_graph(ring_buffer_xml):
    start = ring_buffer_xml.find("<deadlock>")
    end = ring_buffer_xml.find("</deadlock>")
    if start == -1 or end == -1:
        return None
    return ring_buffer_xml[start : end + len("</deadlock>")]


def cleanup():
    conn = connect()
    cur = conn.cursor()
    cur.execute("IF OBJECT_ID('dbo.Day9_Resource1', 'U') IS NOT NULL DROP TABLE dbo.Day9_Resource1")
    cur.execute("IF OBJECT_ID('dbo.Day9_Resource2', 'U') IS NOT NULL DROP TABLE dbo.Day9_Resource2")
    conn.close()


if __name__ == "__main__":
    cmd = sys.argv[1] if len(sys.argv) > 1 else "deadlock"
    os.makedirs(RESULTS_DIR, exist_ok=True)

    if cmd == "cleanup":
        cleanup()
        drop_xe_session()
        print("cleanup done (test tables + XE capture session dropped)")
        sys.exit(0)

    if cmd == "xe-start":
        create_xe_session()
        print(f"XE session {XE_SESSION_NAME} created and started")
        sys.exit(0)

    if cmd == "graph":
        ring_xml = fetch_deadlock_graph()
        raw_path = os.path.join(RESULTS_DIR, "deadlock_ring_buffer.xml")
        with open(raw_path, "w") as f:
            f.write(ring_xml or "<no data>")
        graph = extract_deadlock_graph(ring_xml) if ring_xml else None
        if graph:
            graph_path = os.path.join(RESULTS_DIR, "deadlock_graph.xml")
            with open(graph_path, "w") as f:
                f.write(graph)
            print(f"deadlock graph written to {graph_path} ({len(graph)} chars)")
        else:
            print("no <deadlock> report found in the ring buffer")
        print(f"full ring buffer target data written to {raw_path} ({len(ring_xml or '')} chars)")
        sys.exit(0)

    if cmd == "deadlock":
        result = deadlock_test()
    elif cmd == "fixed":
        result = fixed_test()
    else:
        print(f"unknown command: {cmd}")
        sys.exit(1)

    out_path = os.path.join(RESULTS_DIR, f"{cmd}_result.json")
    with open(out_path, "w") as f:
        json.dump(result, f, indent=2)

    print(json.dumps(result, indent=2))
    print(f"\nFull result written to {out_path}")

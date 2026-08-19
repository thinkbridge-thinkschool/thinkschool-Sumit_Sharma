-- Day-9 Task 1: Non-Repeatable Read anomaly
-- Run schema.sql + seed.sql first. Open two separate connections/tabs
-- (Session A, Session B) against day7quotesdb and run concurrently.

-- ==================== SESSION B - reproduces the anomaly ====================
-- READ COMMITTED only guarantees each individual read sees committed data,
-- not that two reads in the same transaction see the SAME data.
SET TRANSACTION ISOLATION LEVEL READ COMMITTED;
BEGIN TRANSACTION;
SELECT balance FROM dbo.Day9_Accounts WHERE id = 1;   -- read #1, observed: 1000
-- <-- pause here while Session A commits its update below -->
SELECT balance FROM dbo.Day9_Accounts WHERE id = 1;   -- read #2, observed: 2000 (changed!)
COMMIT TRANSACTION;

-- ============================ SESSION A ============================
BEGIN TRANSACTION;
UPDATE dbo.Day9_Accounts SET balance = 2000 WHERE id = 1;
COMMIT TRANSACTION;


-- ==================== SESSION B - anomaly prevented ====================
-- REPEATABLE READ holds Session B's read lock for the whole transaction,
-- so Session A's UPDATE blocks until B finishes -- B's two reads match.
SET TRANSACTION ISOLATION LEVEL REPEATABLE READ;
BEGIN TRANSACTION;
SELECT balance FROM dbo.Day9_Accounts WHERE id = 1;   -- read #1, observed: 1000
-- Session A's UPDATE (below) now blocks on B's shared lock until B commits/rolls back.
SELECT balance FROM dbo.Day9_Accounts WHERE id = 1;   -- read #2, observed: 1000 (unchanged)
COMMIT TRANSACTION;                                    -- only now does A's UPDATE unblock

-- ============================ SESSION A (blocks) ============================
BEGIN TRANSACTION;
UPDATE dbo.Day9_Accounts SET balance = 2000 WHERE id = 1;   -- blocks until B commits
COMMIT TRANSACTION;

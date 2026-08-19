-- Day-9 Task 1: Phantom Read anomaly
-- Run schema.sql + seed.sql first. Open two separate connections/tabs
-- (Session A, Session B) against day7quotesdb and run concurrently.

-- ==================== SESSION B - reproduces the anomaly ====================
-- REPEATABLE READ locks rows it has already read, but does not lock the
-- "gap"/range, so a new row matching the predicate can still appear.
SET TRANSACTION ISOLATION LEVEL REPEATABLE READ;
BEGIN TRANSACTION;
SELECT COUNT(*) FROM dbo.Day9_Orders WHERE customer_id = 42;  -- read #1, observed: 2
-- <-- pause here while Session A inserts a new matching row below -->
SELECT COUNT(*) FROM dbo.Day9_Orders WHERE customer_id = 42;  -- read #2, observed: 3 (phantom!)
COMMIT TRANSACTION;

-- ============================ SESSION A ============================
BEGIN TRANSACTION;
INSERT INTO dbo.Day9_Orders (customer_id, amount) VALUES (42, 200.00);
COMMIT TRANSACTION;


-- ==================== SESSION B - anomaly prevented ====================
-- SERIALIZABLE takes a key-range lock covering customer_id = 42, so Session
-- A's INSERT blocks until B finishes -- B's two reads match.
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
BEGIN TRANSACTION;
SELECT COUNT(*) FROM dbo.Day9_Orders WHERE customer_id = 42;  -- read #1, observed: 2
-- Session A's INSERT (below) now blocks on B's range lock until B commits/rolls back.
SELECT COUNT(*) FROM dbo.Day9_Orders WHERE customer_id = 42;  -- read #2, observed: 2 (unchanged)
COMMIT TRANSACTION;                                            -- only now does A's INSERT unblock

-- ============================ SESSION A (blocks) ============================
BEGIN TRANSACTION;
INSERT INTO dbo.Day9_Orders (customer_id, amount) VALUES (42, 200.00);  -- blocks until B commits
COMMIT TRANSACTION;

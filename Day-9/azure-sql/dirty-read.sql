-- Day-9 Task 1: Dirty Read anomaly
-- Run schema.sql + seed.sql first. Open two separate connections/tabs
-- (Session A, Session B) against day7quotesdb and run concurrently.

-- ============================ SESSION A ============================
-- Starts a transaction, changes the row, but does NOT commit yet.
BEGIN TRANSACTION;
UPDATE dbo.Day9_Accounts SET balance = 9999 WHERE id = 1;
-- <-- pause here (uncommitted) while Session B runs its SELECT below -->
-- then either:
ROLLBACK TRANSACTION;   -- proves the value B saw was never real ("dirty")


-- ==================== SESSION B - reproduces the anomaly ====================
-- READ UNCOMMITTED lets B see A's uncommitted 9999 while A is still mid-transaction.
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;
BEGIN TRANSACTION;
SELECT balance FROM dbo.Day9_Accounts WHERE id = 1;   -- observed: 9999 (dirty)
COMMIT TRANSACTION;


-- ==================== SESSION B - anomaly prevented ====================
-- READ COMMITTED never exposes A's uncommitted row image.
SET TRANSACTION ISOLATION LEVEL READ COMMITTED;
BEGIN TRANSACTION;
SELECT balance FROM dbo.Day9_Accounts WHERE id = 1;   -- observed: 1000 (last committed)
COMMIT TRANSACTION;

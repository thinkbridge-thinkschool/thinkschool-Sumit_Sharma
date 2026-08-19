-- Day 9 Task 2: BROKEN version — reproduces a classic two-resource deadlock.
--
-- Session A locks Resource1 first, then attempts Resource2.
-- Session B locks Resource2 first, then attempts Resource1.
-- Run both sessions concurrently (e.g. two query windows / two connections),
-- with each session's first UPDATE committed to have actually run before the
-- second UPDATE is attempted, so the two sessions hold opposite locks and
-- wait on each other in a circle. SQL Server's deadlock monitor detects the
-- cycle and kills one session as the "deadlock victim" with error 1205.
--
-- This exact SQL was executed by azure-sql/deadlock_test.py using two
-- genuinely concurrent connections, synchronized with threading events so
-- each session provably holds its first lock before the other attempts its
-- second UPDATE (see README.md for the captured evidence).

-- ============================= SESSION A =============================
BEGIN TRANSACTION;
UPDATE dbo.Day9_Resource1 SET value = value + 1 WHERE id = 1;   -- locks Resource1
-- (wait here until Session B has locked Resource2)
UPDATE dbo.Day9_Resource2 SET value = value + 1 WHERE id = 1;   -- blocks on Resource2, held by B
COMMIT TRANSACTION;

-- ============================= SESSION B =============================
BEGIN TRANSACTION;
UPDATE dbo.Day9_Resource2 SET value = value + 1 WHERE id = 1;   -- locks Resource2
-- (wait here until Session A has locked Resource1)
UPDATE dbo.Day9_Resource1 SET value = value + 1 WHERE id = 1;   -- blocks on Resource1, held by A
COMMIT TRANSACTION;

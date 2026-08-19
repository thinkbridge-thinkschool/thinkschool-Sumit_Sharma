-- Day 9 Task 2: FIXED version — consistent lock ordering removes the cycle.
--
-- Both sessions now acquire Resource1 BEFORE Resource2 (the same order).
-- Whichever session gets there first simply makes the other one wait its
-- turn for Resource1; there is no longer a scenario where each session
-- holds what the other one wants, so no circular wait, hence no deadlock.

-- ============================= SESSION A =============================
BEGIN TRANSACTION;
UPDATE dbo.Day9_Resource1 SET value = value + 1 WHERE id = 1;   -- locks Resource1
-- (wait here — same synchronization point as the broken version)
UPDATE dbo.Day9_Resource2 SET value = value + 1 WHERE id = 1;   -- Resource2 is free, proceeds
COMMIT TRANSACTION;

-- ============================= SESSION B =============================
BEGIN TRANSACTION;
UPDATE dbo.Day9_Resource1 SET value = value + 1 WHERE id = 1;   -- blocks until A releases Resource1
UPDATE dbo.Day9_Resource2 SET value = value + 1 WHERE id = 1;   -- Resource2 now free
COMMIT TRANSACTION;

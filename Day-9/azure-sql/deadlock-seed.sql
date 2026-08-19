-- Day 9 Task 2: deterministic seed data, re-run before every deadlock trial.

DELETE FROM dbo.Day9_Resource1;
DELETE FROM dbo.Day9_Resource2;

INSERT INTO dbo.Day9_Resource1 (id, value) VALUES (1, 100);
INSERT INTO dbo.Day9_Resource2 (id, value) VALUES (1, 200);

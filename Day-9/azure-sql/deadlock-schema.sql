-- Day 9 Task 2: deterministic two-resource deadlock test tables.
-- Isolated to this task, dropped again after verification (see README "Cleanup").

IF OBJECT_ID('dbo.Day9_Resource1', 'U') IS NOT NULL DROP TABLE dbo.Day9_Resource1;
IF OBJECT_ID('dbo.Day9_Resource2', 'U') IS NOT NULL DROP TABLE dbo.Day9_Resource2;

CREATE TABLE dbo.Day9_Resource1 (
    id    INT NOT NULL PRIMARY KEY,
    value INT NOT NULL
);

CREATE TABLE dbo.Day9_Resource2 (
    id    INT NOT NULL PRIMARY KEY,
    value INT NOT NULL
);

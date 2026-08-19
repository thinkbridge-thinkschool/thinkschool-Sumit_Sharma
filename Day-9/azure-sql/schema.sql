-- Day-9 Task 1: Isolation Levels + Read Anomalies
-- Schema for the anomaly test tables. Fully isolated from Day-1..Day-8 objects
-- (separate table names, dropped/recreated only within this script).

IF OBJECT_ID('dbo.Day9_Accounts', 'U') IS NOT NULL
    DROP TABLE dbo.Day9_Accounts;

-- Used for the dirty-read and non-repeatable-read tests: a single row whose
-- balance is mutated by Session A while Session B reads it.
CREATE TABLE dbo.Day9_Accounts (
    id      INT NOT NULL PRIMARY KEY,
    balance INT NOT NULL
);

IF OBJECT_ID('dbo.Day9_Orders', 'U') IS NOT NULL
    DROP TABLE dbo.Day9_Orders;

-- Used for the phantom-read test: Session B repeats a ranged SELECT
-- (WHERE customer_id = 42) while Session A inserts a new matching row.
CREATE TABLE dbo.Day9_Orders (
    id          INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    customer_id INT               NOT NULL,
    amount      DECIMAL(10,2)     NOT NULL
);

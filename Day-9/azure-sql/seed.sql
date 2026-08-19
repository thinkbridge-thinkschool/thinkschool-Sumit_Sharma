-- Day-9 Task 1: deterministic seed data for the anomaly tests.
-- Re-run before each test/isolation-level pass so every run starts identical.

TRUNCATE TABLE dbo.Day9_Accounts;
INSERT INTO dbo.Day9_Accounts (id, balance) VALUES (1, 1000);

TRUNCATE TABLE dbo.Day9_Orders;
INSERT INTO dbo.Day9_Orders (customer_id, amount) VALUES
    (42, 100.00),
    (42, 150.00),
    (7,   50.00),
    (7,   75.00);

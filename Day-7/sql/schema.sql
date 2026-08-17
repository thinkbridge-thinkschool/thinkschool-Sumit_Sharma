-- Day-7 schema.sql
--
-- Copied from the real QuotesApi "Quotes" table schema, as tracked in
-- Day-4/QuotesApi/quotes.db (EF Core migrations InitialCreate +
-- AddQuoteSoftDelete). Only the Quotes table is copied here because this
-- task only concerns authors and their quotes; Users/Collections/
-- RefreshTokens from the same database are not needed and are left
-- untouched in Day-4/Day-5.
--
-- Source of truth verified with:
--   sqlite3 Day-4/QuotesApi/quotes.db ".schema Quotes"

CREATE TABLE "Quotes" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_Quotes" PRIMARY KEY AUTOINCREMENT,
    "Author" TEXT NOT NULL,
    "Text" TEXT NOT NULL,
    "IsDeleted" INTEGER NOT NULL DEFAULT 0
);

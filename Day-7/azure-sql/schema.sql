-- Day-7 Azure SQL: schema.sql
--
-- T-SQL port of the local SQLite Day-7 schema (see Day-7/sql/schema.sql,
-- Day-7/sql/window-functions-seed.sql, Day-7/sql/set-operations-seed.sql).
-- This is NOT a copy-paste of the SQLite DDL - types and identity semantics
-- are converted to their Azure SQL / SQL Server equivalents:
--
--   INTEGER PRIMARY KEY AUTOINCREMENT -> INT IDENTITY(1,1) PRIMARY KEY
--   TEXT                              -> NVARCHAR(n) / NVARCHAR(MAX)
--   INTEGER (0/1 flag)                -> BIT
--   TEXT date ('YYYY-MM-DD')          -> DATE
--
-- Run this once against the target Azure SQL database before seed.sql.

CREATE TABLE Quotes (
    Id        INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Quotes PRIMARY KEY,
    Author    NVARCHAR(200) NOT NULL,
    Text      NVARCHAR(MAX) NOT NULL,
    IsDeleted BIT NOT NULL CONSTRAINT DF_Quotes_IsDeleted DEFAULT (0)
);
GO

-- Day-7-only exercise table (Task 2): pairs real Quotes rows with a
-- synthetic CreatedAt date, since the real Quotes table has no timestamp.
CREATE TABLE QuotesTimeline (
    Id        INT NOT NULL CONSTRAINT PK_QuotesTimeline PRIMARY KEY,
    QuoteId   INT NOT NULL CONSTRAINT FK_QuotesTimeline_Quotes REFERENCES Quotes(Id),
    Author    NVARCHAR(200) NOT NULL,
    Text      NVARCHAR(MAX) NOT NULL,
    CreatedAt DATE NOT NULL
);
GO

-- Day-7-only exercise tables (Task 3): tags and author classifications.
CREATE TABLE Tags (
    Id   INT NOT NULL CONSTRAINT PK_Tags PRIMARY KEY,
    Name NVARCHAR(100) NOT NULL CONSTRAINT UQ_Tags_Name UNIQUE
);
GO

CREATE TABLE QuoteTags (
    QuoteId INT NOT NULL CONSTRAINT FK_QuoteTags_Quotes REFERENCES Quotes(Id),
    TagId   INT NOT NULL CONSTRAINT FK_QuoteTags_Tags REFERENCES Tags(Id),
    CONSTRAINT PK_QuoteTags PRIMARY KEY (QuoteId, TagId)
);
GO

CREATE TABLE AuthorCategories (
    Author   NVARCHAR(200) NOT NULL,
    Category NVARCHAR(20) NOT NULL CONSTRAINT CK_AuthorCategories_Category CHECK (Category IN ('classic', 'modern')),
    CONSTRAINT PK_AuthorCategories PRIMARY KEY (Author, Category)
);
GO

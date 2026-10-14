-- Equivalent of what the API creates on startup in Development (Database:EnsureCreated).
-- Run manually for other environments.
IF DB_ID('ProductDb') IS NULL CREATE DATABASE ProductDb;
GO
USE ProductDb;
GO
IF OBJECT_ID('dbo.Products') IS NULL
BEGIN
    CREATE TABLE dbo.Products (
        Id           INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Products PRIMARY KEY,
        Sku          NVARCHAR(50)   NOT NULL,
        Name         NVARCHAR(200)  NOT NULL,
        Description  NVARCHAR(2000) NULL,
        Category     NVARCHAR(100)  NULL,
        Price        DECIMAL(18,2)  NOT NULL,
        Quantity     INT            NOT NULL,
        CreatedAtUtc DATETIME2      NOT NULL CONSTRAINT DF_Products_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
        UpdatedAtUtc DATETIME2      NOT NULL CONSTRAINT DF_Products_UpdatedAtUtc DEFAULT SYSUTCDATETIME()
    );
    CREATE UNIQUE INDEX IX_Products_Sku ON dbo.Products (Sku);
    CREATE INDEX IX_Products_Category ON dbo.Products (Category);
END
GO

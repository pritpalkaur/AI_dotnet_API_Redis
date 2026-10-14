-- Bulk-insert test products so the cache benefit is measurable.
-- Re-runnable: removes previous SEED-* rows first.
--   sqlcmd -S localhost -U <user> -P <password> -d ProductDb -i scripts\seed.sql -v Rows=200000
SET NOCOUNT ON;

DELETE FROM dbo.Products WHERE Sku LIKE 'SEED-%';

DECLARE @categories TABLE (Idx INT PRIMARY KEY, Name NVARCHAR(100));
INSERT INTO @categories VALUES
 (0,'Peripherals'),(1,'Displays'),(2,'Storage'),(3,'Networking'),(4,'Audio'),
 (5,'Laptops'),(6,'Desktops'),(7,'Tablets'),(8,'Phones'),(9,'Cameras'),
 (10,'Printers'),(11,'Gaming'),(12,'Wearables'),(13,'Smart Home'),(14,'Cables'),
 (15,'Power'),(16,'Software'),(17,'Office'),(18,'Furniture'),(19,'Accessories');

;WITH n AS (
    SELECT TOP ($(Rows)) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS i
    FROM sys.all_objects a CROSS JOIN sys.all_objects b
)
INSERT INTO dbo.Products (Sku, Name, Description, Category, Price, Quantity)
SELECT
    CONCAT('SEED-', RIGHT(CONCAT('0000000', i), 7)),
    CONCAT(c.Name, ' item ', i),
    CONCAT('Generated test product #', i, ' in ', c.Name, '. ', REPLICATE('Lorem ipsum dolor sit amet. ', 5)),
    c.Name,
    CAST(5 + (i * 7919 % 99500) / 100.0 AS DECIMAL(18,2)),
    i * 31 % 500
FROM n
JOIN @categories c ON c.Idx = n.i % 20;

SELECT COUNT(*) AS TotalProducts FROM dbo.Products;

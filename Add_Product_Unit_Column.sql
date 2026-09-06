-- =============================================
-- Script: Add Unit Column to Products Table
-- Description: Adds a Unit column to store product unit types (piece, kg, liter, etc.)
-- Date: 2026-01-25
-- =============================================

USE [POS];  -- Change database name if different
GO

-- Check if the column already exists before adding it
IF NOT EXISTS (
    SELECT 1 
    FROM INFORMATION_SCHEMA.COLUMNS 
    WHERE TABLE_NAME = 'Products' 
    AND COLUMN_NAME = 'Unit'
)
BEGIN
    -- Add the Unit column
    ALTER TABLE [Products]
    ADD [Unit] NVARCHAR(50) NULL;
    
    PRINT 'Unit column added successfully to Products table.';
END
ELSE
BEGIN
    PRINT 'Unit column already exists in Products table.';
END
GO

-- Optional: Update existing products with default unit if needed
-- Uncomment and modify as needed
/*
UPDATE [Products]
SET [Unit] = 'piece'  -- or 'kg', 'liter', etc.
WHERE [Unit] IS NULL;
GO
*/

-- =============================================
-- Rollback Script (if needed)
-- =============================================
/*
-- To remove the Unit column, run this:
IF EXISTS (
    SELECT 1 
    FROM INFORMATION_SCHEMA.COLUMNS 
    WHERE TABLE_NAME = 'Products' 
    AND COLUMN_NAME = 'Unit'
)
BEGIN
    ALTER TABLE [Products]
    DROP COLUMN [Unit];
    
    PRINT 'Unit column removed from Products table.';
END
GO
*/

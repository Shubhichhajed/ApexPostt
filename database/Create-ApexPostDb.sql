/* Run from master in SSMS. This creates only the dedicated database; EF migrations create its schema. */
USE master;
GO
IF DB_ID(N'ApexPostDb') IS NULL
BEGIN
    CREATE DATABASE ApexPostDb;
END;
GO

-- Run in your intended, empty application database. This script is repeatable.
IF OBJECT_ID('dbo.Locations','U') IS NULL
CREATE TABLE dbo.Locations (
 Location nvarchar(100) COLLATE Latin1_General_100_BIN2 NOT NULL CONSTRAINT PK_Locations PRIMARY KEY,
 LastSuccessAtUtc datetimeoffset NULL,
 LastAttemptAtUtc datetimeoffset NULL,
 FailureCode nvarchar(80) NULL
);
IF OBJECT_ID('dbo.Listings','U') IS NULL
CREATE TABLE dbo.Listings (
 Id uniqueidentifier NOT NULL CONSTRAINT PK_Listings PRIMARY KEY,
 Location nvarchar(100) COLLATE Latin1_General_100_BIN2 NOT NULL REFERENCES dbo.Locations(Location),
 IdentityHash binary(32) NOT NULL,
 IdentityText nvarchar(max) NOT NULL,
 DetailsJson nvarchar(max) NOT NULL CHECK(ISJSON(DetailsJson)=1),
 IsActive bit NOT NULL,
 Discovery varchar(10) NOT NULL CHECK(Discovery IN ('new','existing','returned')),
 FirstSeenAtUtc datetimeoffset NOT NULL,
 LastSeenAtUtc datetimeoffset NOT NULL,
 InactivatedAtUtc datetimeoffset NULL,
 CONSTRAINT UQ_Listings_Identity UNIQUE(Location,IdentityHash)
);
-- No initial firms: first successful source collection populates listings.
-- Default cities belong to the editable UI selection, not seed business data.

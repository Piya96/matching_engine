-- Hand-authored SQL Server DDL for the MatchingEngine schema.
--
-- Why this file exists instead of an EF Core migration: `dotnet ef
-- migrations add` needs the .NET SDK, which isn't installable in the
-- sandbox this repo was built in. This script is written by hand to match
-- MatchingEngine.Data/Configurations/*.cs field-for-field and constraint-
-- for-constraint -- if you have the SDK, prefer generating a real
-- migration from the DbContext instead of trusting this file's authors to
-- have kept the two in sync by hand forever. It's here so the schema (and
-- the docker-compose SQL Server instance) is something you can actually
-- stand up and query without the SDK at all.
--
-- Run by scripts/init-db.sh once SQL Server is accepting connections.

IF NOT EXISTS (SELECT * FROM sys.databases WHERE name = 'MatchingEngine')
BEGIN
    CREATE DATABASE MatchingEngine;
END
GO

USE MatchingEngine;
GO

CREATE TABLE Entities (
    Id           INT IDENTITY(1,1) PRIMARY KEY,
    ExternalRef  NVARCHAR(200)  NOT NULL,
    EntityType   NVARCHAR(100)  NOT NULL,
    CreatedUtc   DATETIME2      NOT NULL DEFAULT SYSUTCDATETIME()
);
GO
-- Unique so ingestion can upsert on (EntityType, ExternalRef) without a
-- separate existence check; also the index the "find my entity" read uses.
CREATE UNIQUE INDEX IX_Entities_EntityType_ExternalRef ON Entities (EntityType, ExternalRef);
GO

CREATE TABLE CriteriaSets (
    Id          INT IDENTITY(1,1) PRIMARY KEY,
    Name        NVARCHAR(150) NOT NULL,
    Version     INT           NOT NULL DEFAULT 1,
    IsActive    BIT           NOT NULL DEFAULT 1,
    CreatedUtc  DATETIME2     NOT NULL DEFAULT SYSUTCDATETIME()
);
GO
CREATE UNIQUE INDEX IX_CriteriaSets_Name_Version ON CriteriaSets (Name, Version);
GO

CREATE TABLE Criteria (
    Id               INT IDENTITY(1,1) PRIMARY KEY,
    CriteriaSetId    INT           NOT NULL,
    AttributeName    NVARCHAR(150) NOT NULL,
    -- Stored as the enum's string name, not its int -- see
    -- CriterionConfiguration.cs. The batch SQL in MatchingEngine.Core/Batch
    -- matches these exact string literals ('Equals', 'GreaterThan', ...).
    Operator         NVARCHAR(30)  NOT NULL,
    ValueType        NVARCHAR(20)  NOT NULL,
    TargetValue      NVARCHAR(500) NULL,
    -- JSON array, e.g. ["NL","DE","BE"]. Only meaningful for Operator = 'In'.
    TargetValueList  NVARCHAR(MAX) NOT NULL DEFAULT '[]',
    Weight           FLOAT         NOT NULL DEFAULT 1.0,
    IsRequired       BIT           NOT NULL DEFAULT 0,
    CONSTRAINT FK_Criteria_CriteriaSets FOREIGN KEY (CriteriaSetId)
        REFERENCES CriteriaSets (Id) ON DELETE CASCADE
);
GO
CREATE INDEX IX_Criteria_CriteriaSetId ON Criteria (CriteriaSetId);
GO

CREATE TABLE EntityAttributes (
    Id              INT IDENTITY(1,1) PRIMARY KEY,
    EntityId        INT           NOT NULL,
    AttributeName   NVARCHAR(150) NOT NULL,
    AttributeValue  NVARCHAR(MAX) NULL,
    ValueType       NVARCHAR(20)  NOT NULL,
    CONSTRAINT FK_EntityAttributes_Entities FOREIGN KEY (EntityId)
        REFERENCES Entities (Id) ON DELETE CASCADE
);
GO
-- The index the whole engine's read performance rides on: every Criterion
-- evaluation is "find this Entity's value for this AttributeName". Unique
-- because an attribute is current-state, not a history.
CREATE UNIQUE INDEX IX_EntityAttributes_EntityId_AttributeName ON EntityAttributes (EntityId, AttributeName);
GO

CREATE TABLE MatchResults (
    Id                INT IDENTITY(1,1) PRIMARY KEY,
    EntityId          INT       NOT NULL,
    CriteriaSetId     INT       NOT NULL,
    TotalScore        FLOAT     NOT NULL,
    MaxPossibleScore  FLOAT     NOT NULL,
    Passed            BIT       NOT NULL,
    EvaluatedUtc      DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_MatchResults_Entities FOREIGN KEY (EntityId)
        REFERENCES Entities (Id),        -- NO ACTION: keep a match's history even if policy ever allowed entity deletes
    CONSTRAINT FK_MatchResults_CriteriaSets FOREIGN KEY (CriteriaSetId)
        REFERENCES CriteriaSets (Id)     -- NO ACTION: a fired CriteriaSet is never deleted, only superseded by a new Version
);
GO
-- Descending on EvaluatedUtc: "most recent match for this entity against
-- this rule set" becomes a backward index seek instead of a scan-and-sort.
CREATE INDEX IX_MatchResults_Entity_CriteriaSet_EvaluatedUtc
    ON MatchResults (EntityId, CriteriaSetId, EvaluatedUtc DESC);
GO

CREATE TABLE MatchResultDetails (
    Id              INT IDENTITY(1,1) PRIMARY KEY,
    MatchResultId   INT   NOT NULL,
    CriterionId     INT   NOT NULL,
    Satisfied       BIT   NOT NULL,
    PointsAwarded   FLOAT NOT NULL,
    CONSTRAINT FK_MatchResultDetails_MatchResults FOREIGN KEY (MatchResultId)
        REFERENCES MatchResults (Id) ON DELETE CASCADE,
    CONSTRAINT FK_MatchResultDetails_Criteria FOREIGN KEY (CriterionId)
        REFERENCES Criteria (Id)          -- NO ACTION: keep the audit trail even across a criterion edit/retire
);
GO
CREATE INDEX IX_MatchResultDetails_MatchResultId ON MatchResultDetails (MatchResultId);
GO

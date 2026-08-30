-- Seeds the exact scenario src/MatchingEngine.Demo/Program.cs runs
-- in-memory: same criteria set, same four suppliers. The point is
-- symmetry -- if you have SQL Server, running the batch path
-- (BatchScoreQueryBuilder.RunAsync) against this seeded data should
-- reproduce exactly the per-entity totals the Demo project prints (see
-- README "Sample output"), because both paths implement the same oracle.
--
-- Run after schema.sql, via scripts/init-db.sh.

USE MatchingEngine;
GO

INSERT INTO CriteriaSets (Name, Version, IsActive) VALUES ('SupplierEligibility', 1, 1);
DECLARE @CriteriaSetId INT = SCOPE_IDENTITY();

INSERT INTO Criteria (CriteriaSetId, AttributeName, Operator, ValueType, TargetValue, TargetValueList, Weight, IsRequired)
VALUES
    (@CriteriaSetId, 'certification_tier', 'Equals',              'String', 'Gold',  '[]',                3, 0),
    (@CriteriaSetId, 'annual_volume',      'GreaterThanOrEqual',  'Number', '1000',  '[]',                2, 0),
    (@CriteriaSetId, 'region',             'In',                  'String', NULL,    '["NL","DE","BE"]',  2, 0),
    (@CriteriaSetId, 'notes',              'Contains',            'String', 'priority', '[]',             1, 0),
    (@CriteriaSetId, 'blacklisted',        'Equals',              'String', 'false', '[]',                0, 1);
GO

INSERT INTO Entities (ExternalRef, EntityType) VALUES
    ('SUP-001', 'Supplier'),
    ('SUP-002', 'Supplier'),
    ('SUP-003', 'Supplier'),
    ('SUP-004', 'Supplier');
GO

INSERT INTO EntityAttributes (EntityId, AttributeName, AttributeValue, ValueType)
SELECT e.Id, v.AttributeName, v.AttributeValue, v.ValueType
FROM Entities e
CROSS APPLY (VALUES
    -- SUP-001: satisfies everything
    ('SUP-001', 'certification_tier', 'Gold',                     'String'),
    ('SUP-001', 'annual_volume',      '1500',                     'Number'),
    ('SUP-001', 'region',             'NL',                       'String'),
    ('SUP-001', 'notes',              'high priority account',    'String'),
    ('SUP-001', 'blacklisted',        'false',                    'String'),
    -- SUP-002: wrong tier, notes don't mention priority
    ('SUP-002', 'certification_tier', 'Silver',                   'String'),
    ('SUP-002', 'annual_volume',      '2200',                     'Number'),
    ('SUP-002', 'region',             'DE',                       'String'),
    ('SUP-002', 'notes',              'standard account',         'String'),
    ('SUP-002', 'blacklisted',        'false',                    'String'),
    -- SUP-003: blacklisted (required criterion fails -> Passed = false) and wrong region
    ('SUP-003', 'certification_tier', 'Gold',                     'String'),
    ('SUP-003', 'annual_volume',      '5000',                     'Number'),
    ('SUP-003', 'region',             'FR',                       'String'),
    ('SUP-003', 'notes',              'priority review pending',  'String'),
    ('SUP-003', 'blacklisted',        'true',                     'String'),
    -- SUP-004: annual_volume never collected, notes never collected -- exercises the LEFT JOIN NULL case
    ('SUP-004', 'certification_tier', 'Gold',                     'String'),
    ('SUP-004', 'region',             'BE',                       'String'),
    ('SUP-004', 'blacklisted',        'false',                    'String')
) AS v(ExternalRef, AttributeName, AttributeValue, ValueType)
WHERE e.ExternalRef = v.ExternalRef;
GO

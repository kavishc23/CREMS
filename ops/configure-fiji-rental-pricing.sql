-- One-time authorized catalogue setup; no existing transactions are repriced.
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF EXISTS (SELECT 1 FROM SystemSettings WITH (UPDLOCK, HOLDLOCK) WHERE [Key] = 'rentals.fijiPricingDefaults.20260924')
BEGIN
    COMMIT;
    RETURN;
END;
UPDATE Divisions SET DefaultTaxRate = 12.5, UpdatedAt = SYSDATETIMEOFFSET(),
    DefaultBondAmount = CASE WHEN DefaultBondAmount <> 0 THEN DefaultBondAmount
        WHEN Code = 'MOTORS' THEN 1000 WHEN Code = 'CARPTRAC' THEN 2000 ELSE 300 END
WHERE Code IN ('MOTORS','CARPTRAC','SHIPPING');
UPDATE s SET DefaultDepositAmount = CASE s.Code
    WHEN 'VEHICLE_RENTAL' THEN 1000 WHEN 'EQUIPMENT_HIRE' THEN 2000
    WHEN 'SCAFFOLDING_HIRE' THEN 1000 ELSE 300 END, UpdatedAt = SYSDATETIMEOFFSET()
FROM ServiceOfferings s JOIN Divisions d ON d.Id = s.DivisionId
WHERE d.Code IN ('MOTORS','CARPTRAC','SHIPPING') AND s.DefaultDepositAmount = 0
    AND s.Code IN ('VEHICLE_RENTAL','EQUIPMENT_HIRE','SCAFFOLDING_HIRE','PORTABLE_TOILET_HIRE','BIG_BIN_HIRE');
UPDATE a SET DefaultBondAmount = CASE
    WHEN a.Type IN (0,2,3) THEN CASE
        WHEN a.Name LIKE '%Sedan%' OR a.Name LIKE '%Hatchback%' OR a.Name LIKE '%Wagon%' THEN 1000
        WHEN a.Name LIKE '%Coach%' OR a.Name LIKE '%Cargo%' THEN 3000 ELSE 2000 END
    WHEN a.Type = 4 THEN 5000 WHEN a.Type IN (5,6) THEN 2000
    WHEN a.Type = 8 THEN 1000 WHEN a.Type IN (9,10) THEN 300 ELSE 500 END,
    InheritBond = 0, UpdatedAt = SYSDATETIMEOFFSET()
FROM Assets a JOIN Divisions d ON d.Id = a.DivisionId
WHERE d.Code IN ('MOTORS','CARPTRAC','SHIPPING') AND a.DefaultBondAmount = 0;
UPDATE SystemSettings SET Value = '12.5', UpdatedAt = SYSDATETIMEOFFSET()
WHERE [Key] = 'rentals.vatRate';
INSERT INTO SystemSettings (Id,[Key],Value,Category,Description,IsSecret,CreatedAt)
VALUES (NEWID(),'rentals.fijiPricingDefaults.20260924','Applied','Rental',
    'User-authorized VAT and category bond setup; see docs/rental-pricing-defaults.md.',0,SYSDATETIMEOFFSET());
COMMIT;

-- ============================================================
-- SS (SSDIGI) — OmniPay + AppVersion migration script
-- Run once against the SSDIGI database.
-- All statements are idempotent (IF NOT EXISTS guards).
-- ============================================================


-- ────────────────────────────────────────────────────────────
-- 1. Bledger — add FOmniTransactionId column
--    Used by InsertBledgerPublic for idempotency.
--    Mirrors FRazorpayPaymentId in the JJK Bledger table.
-- ────────────────────────────────────────────────────────────
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('dbo.Bledger')
      AND name = 'FOmniTransactionId'
)
BEGIN
    ALTER TABLE dbo.Bledger
        ADD FOmniTransactionId NVARCHAR(100) NULL;

    PRINT 'Added column Bledger.FOmniTransactionId';
END
ELSE
    PRINT 'Column Bledger.FOmniTransactionId already exists — skipped.';
GO


-- ────────────────────────────────────────────────────────────
-- 2. OmniPendingPayments
--    Staging table: app writes here (via save-pending) before
--    opening the Omniware payment URL.  The record callback
--    reads ChitPayload from here and runs the Bledger insert.
-- ────────────────────────────────────────────────────────────
IF NOT EXISTS (
    SELECT 1 FROM sys.objects
    WHERE object_id = OBJECT_ID('dbo.OmniPendingPayments')
      AND type = 'U'
)
BEGIN
    CREATE TABLE dbo.OmniPendingPayments
    (
        Id            INT           NOT NULL IDENTITY(1,1) PRIMARY KEY,
        OrderId       NVARCHAR(50)  NOT NULL,           -- OMN<timestamp> from create-order
        TransactionId NVARCHAR(100) NULL,               -- filled by record callback on success
        UserId        NVARCHAR(100) NULL,               -- internal app user id (udf1)
        ChitPayload   NVARCHAR(MAX) NULL,               -- JSON-serialised ChitSchemeModel
        Status        NVARCHAR(20)  NOT NULL            -- pending | processing | completed | failed | needs_review
                        DEFAULT 'pending',
        CreatedAt     DATETIME      NOT NULL DEFAULT GETDATE(),
        ProcessedAt   DATETIME      NULL,
        ErrorMessage  NVARCHAR(MAX) NULL
    );

    -- Fast lookup by OrderId (the natural join key between
    -- create-order, save-pending, and the record callback)
    CREATE UNIQUE INDEX UX_OmniPendingPayments_OrderId
        ON dbo.OmniPendingPayments (OrderId);

    PRINT 'Created table dbo.OmniPendingPayments';
END
ELSE
    PRINT 'Table dbo.OmniPendingPayments already exists — skipped.';
GO


-- ────────────────────────────────────────────────────────────
-- 3. AppVersion
--    One row per (Platform, AppType) combination.
--    AppVersionController reads/writes this table.
-- ────────────────────────────────────────────────────────────
IF NOT EXISTS (
    SELECT 1 FROM sys.objects
    WHERE object_id = OBJECT_ID('dbo.AppVersion')
      AND type = 'U'
)
BEGIN
    CREATE TABLE dbo.AppVersion
    (
        Id            INT           NOT NULL IDENTITY(1,1) PRIMARY KEY,
        Platform      NVARCHAR(10)  NOT NULL,   -- 'android' | 'ios'
        AppType       NVARCHAR(10)  NOT NULL    -- 'user'    | 'admin'
                        DEFAULT 'user',
        Version       NVARCHAR(20)  NOT NULL,   -- e.g. '1.2.0'
        VersionCode   INT           NOT NULL,   -- integer build number for comparison
        IsMandatory   BIT           NOT NULL DEFAULT 0,
        UpdateMessage NVARCHAR(500) NULL,
        StoreUrl      NVARCHAR(500) NULL,
        IsActive      BIT           NOT NULL DEFAULT 1,
        UpdatedAt     DATETIME2     NOT NULL DEFAULT SYSUTCDATETIME()
    );

    -- Enforce one active row per platform+appType so the
    -- MERGE in AppVersionController.UpdateVersion is safe.
    CREATE UNIQUE INDEX UX_AppVersion_Platform_AppType
        ON dbo.AppVersion (Platform, AppType);

    -- Seed initial rows (update Version/VersionCode/StoreUrl as needed)
    INSERT INTO dbo.AppVersion
        (Platform, AppType, Version, VersionCode, IsMandatory, UpdateMessage, StoreUrl, IsActive)
    VALUES
        ('android', 'user',  '1.0.0', 1, 0, 'Initial release.', NULL, 1),
        ('android', 'admin', '1.0.0', 1, 0, 'Initial release.', NULL, 1),
        ('ios',     'user',  '1.0.0', 1, 0, 'Initial release.', NULL, 1),
        ('ios',     'admin', '1.0.0', 1, 0, 'Initial release.', NULL, 1);

    PRINT 'Created table dbo.AppVersion and seeded initial rows.';
END
ELSE
    PRINT 'Table dbo.AppVersion already exists — skipped.';
GO


-- ────────────────────────────────────────────────────────────
-- Summary of changes expected in Bledger
-- ────────────────────────────────────────────────────────────
-- Column       Type            Purpose
-- ------------ --------------- --------------------------------
-- FOmniTransactionId  NVARCHAR(100) NULL
--              Omniware transaction_id from the payment callback.
--              InsertChitScheme and the record callback use this
--              for idempotency (SELECT TOP 1 fVouchno FROM Bledger
--              WHERE FOmniTransactionId = @tid AND fBillType = 'CT').
-- ────────────────────────────────────────────────────────────


-- ────────────────────────────────────────────────────────────
-- 4. Bledger — add FRazorpayPaymentId column
--    Used by razorpay-webhook for idempotency (mirrors JJK).
-- ────────────────────────────────────────────────────────────
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('dbo.Bledger')
      AND name = 'FRazorpayPaymentId'
)
BEGIN
    ALTER TABLE dbo.Bledger
        ADD FRazorpayPaymentId NVARCHAR(100) NULL;

    PRINT 'Added column Bledger.FRazorpayPaymentId';
END
ELSE
    PRINT 'Column Bledger.FRazorpayPaymentId already exists — skipped.';
GO


-- ────────────────────────────────────────────────────────────
-- 5. PendingPayments  (Razorpay)
--    App writes here before opening checkout sheet.
--    razorpay-webhook reads ChitPayload and runs Bledger insert.
--    verify-payment marks row as 'processing'.
-- ────────────────────────────────────────────────────────────
IF NOT EXISTS (
    SELECT 1 FROM sys.objects
    WHERE object_id = OBJECT_ID('dbo.PendingPayments')
      AND type = 'U'
)
BEGIN
    CREATE TABLE dbo.PendingPayments
    (
        Id                INT           NOT NULL IDENTITY(1,1) PRIMARY KEY,
        RazorpayOrderId   NVARCHAR(100) NOT NULL,
        RazorpayPaymentId NVARCHAR(100) NULL,           -- filled after payment
        UserId            NVARCHAR(100) NULL,
        ChitPayload       NVARCHAR(MAX) NULL,            -- JSON ChitSchemeModel
        Status            NVARCHAR(20)  NOT NULL
                              DEFAULT 'pending',         -- pending|processing|completed|failed|needs_review
        CreatedAt         DATETIME      NOT NULL DEFAULT GETDATE(),
        ProcessedAt       DATETIME      NULL,
        ErrorMessage      NVARCHAR(MAX) NULL
    );

    CREATE UNIQUE INDEX UX_PendingPayments_RazorpayOrderId
        ON dbo.PendingPayments (RazorpayOrderId);

    PRINT 'Created table dbo.PendingPayments';
END
ELSE
    PRINT 'Table dbo.PendingPayments already exists — skipped.';
GO


-- ────────────────────────────────────────────────────────────
-- 6. RazorpayWebhookEvents
--    One row per event delivery.  UNIQUE(EventId) ensures
--    at-least-once delivery from Razorpay is safely deduplicated.
-- ────────────────────────────────────────────────────────────
IF NOT EXISTS (
    SELECT 1 FROM sys.objects
    WHERE object_id = OBJECT_ID('dbo.RazorpayWebhookEvents')
      AND type = 'U'
)
BEGIN
    CREATE TABLE dbo.RazorpayWebhookEvents
    (
        Id          INT            NOT NULL IDENTITY(1,1) PRIMARY KEY,
        EventId     NVARCHAR(100)  NOT NULL,    -- X-Razorpay-Event-Id header
        EventType   NVARCHAR(100)  NULL,        -- e.g. 'payment.captured'
        PaymentId   NVARCHAR(100)  NULL,
        OrderId     NVARCHAR(100)  NULL,
        Status      NVARCHAR(20)   NOT NULL DEFAULT 'received',  -- received|processed|needs_review
        ReceivedAt  DATETIME2      NOT NULL DEFAULT SYSUTCDATETIME(),
        ProcessedAt DATETIME2      NULL,
        ErrorMessage NVARCHAR(MAX) NULL
    );

    CREATE UNIQUE INDEX UX_RazorpayWebhookEvents_EventId
        ON dbo.RazorpayWebhookEvents (EventId);

    PRINT 'Created table dbo.RazorpayWebhookEvents';
END
ELSE
    PRINT 'Table dbo.RazorpayWebhookEvents already exists — skipped.';
GO

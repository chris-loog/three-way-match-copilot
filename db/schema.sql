IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF SCHEMA_ID(N'ap') IS NULL EXEC(N'CREATE SCHEMA [ap];');

IF SCHEMA_ID(N'erp') IS NULL EXEC(N'CREATE SCHEMA [erp];');

CREATE TABLE [ap].[Document] (
    [Id] int NOT NULL IDENTITY,
    [BlobPath] nvarchar(500) NOT NULL,
    [Type] nvarchar(30) NOT NULL,
    [Status] nvarchar(30) NOT NULL,
    [ContentHash] nchar(64) NOT NULL,
    [ErrorMessage] nvarchar(2000) NULL,
    [ReceivedAt] datetimeoffset NOT NULL,
    [PeriodEnd] datetime2 GENERATED ALWAYS AS ROW END HIDDEN NOT NULL,
    [PeriodStart] datetime2 GENERATED ALWAYS AS ROW START HIDDEN NOT NULL,
    CONSTRAINT [PK_Document] PRIMARY KEY ([Id]),
    PERIOD FOR SYSTEM_TIME([PeriodStart], [PeriodEnd])
) WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = [ap].[DocumentHistory]));

CREATE TABLE [erp].[Item] (
    [Id] int NOT NULL IDENTITY,
    [Code] nvarchar(50) NOT NULL,
    [Name] nvarchar(200) NOT NULL,
    [BaseUnit] nvarchar(20) NOT NULL,
    CONSTRAINT [PK_Item] PRIMARY KEY ([Id])
);

CREATE TABLE [erp].[Supplier] (
    [Id] int NOT NULL IDENTITY,
    [Name] nvarchar(200) NOT NULL,
    [Category] nvarchar(50) NOT NULL,
    [VatNumber] nvarchar(20) NOT NULL,
    [RegistryCode] nvarchar(20) NOT NULL,
    [Iban] nvarchar(34) NOT NULL,
    [DocumentCulture] nvarchar(10) NOT NULL,
    [PriceTolerancePercent] decimal(5,2) NULL,
    [QuantityTolerancePercent] decimal(5,2) NULL,
    CONSTRAINT [PK_Supplier] PRIMARY KEY ([Id])
);

CREATE TABLE [ap].[ExtractedDocument] (
    [Id] int NOT NULL IDENTITY,
    [DocumentId] int NOT NULL,
    [ModelId] nvarchar(100) NOT NULL,
    [SupplierName] nvarchar(200) NULL,
    [SupplierVatNumber] nvarchar(30) NULL,
    [DocumentNumber] nvarchar(50) NULL,
    [PurchaseOrderReference] nvarchar(50) NULL,
    [DocumentDate] date NULL,
    [NetTotal] decimal(19,4) NULL,
    [VatTotal] decimal(19,4) NULL,
    [GrossTotal] decimal(19,4) NULL,
    [Currency] nchar(3) NULL,
    [Iban] nvarchar(50) NULL,
    [MinKeyFieldConfidence] float NULL,
    [FieldConfidenceJson] nvarchar(max) NULL,
    [RawJson] nvarchar(max) NOT NULL,
    [ExtractedAt] datetimeoffset NOT NULL,
    CONSTRAINT [PK_ExtractedDocument] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_ExtractedDocument_Document_DocumentId] FOREIGN KEY ([DocumentId]) REFERENCES [ap].[Document] ([Id]) ON DELETE CASCADE
);

CREATE TABLE [erp].[PurchaseOrder] (
    [Id] int NOT NULL IDENTITY,
    [Number] nvarchar(30) NOT NULL,
    [SupplierId] int NOT NULL,
    [OrderDate] date NOT NULL,
    [Currency] nchar(3) NOT NULL,
    CONSTRAINT [PK_PurchaseOrder] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_PurchaseOrder_Supplier_SupplierId] FOREIGN KEY ([SupplierId]) REFERENCES [erp].[Supplier] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [erp].[SupplierItem] (
    [Id] int NOT NULL IDENTITY,
    [SupplierId] int NOT NULL,
    [ItemId] int NOT NULL,
    [SupplierItemCode] nvarchar(50) NOT NULL,
    [SupplierDescription] nvarchar(300) NOT NULL,
    [SupplierUnit] nvarchar(20) NOT NULL,
    [ConversionFactorToBase] decimal(18,6) NOT NULL,
    CONSTRAINT [PK_SupplierItem] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_SupplierItem_Item_ItemId] FOREIGN KEY ([ItemId]) REFERENCES [erp].[Item] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_SupplierItem_Supplier_SupplierId] FOREIGN KEY ([SupplierId]) REFERENCES [erp].[Supplier] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [ap].[ExtractedLine] (
    [Id] int NOT NULL IDENTITY,
    [ExtractedDocumentId] int NOT NULL,
    [LineNumber] int NOT NULL,
    [SupplierItemCode] nvarchar(50) NULL,
    [Description] nvarchar(500) NULL,
    [Quantity] decimal(18,4) NULL,
    [Unit] nvarchar(20) NULL,
    [UnitPrice] decimal(19,4) NULL,
    [VatRate] decimal(5,4) NULL,
    [LineTotal] decimal(19,4) NULL,
    [Confidence] float NULL,
    CONSTRAINT [PK_ExtractedLine] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_ExtractedLine_ExtractedDocument_ExtractedDocumentId] FOREIGN KEY ([ExtractedDocumentId]) REFERENCES [ap].[ExtractedDocument] ([Id]) ON DELETE CASCADE
);

CREATE TABLE [erp].[GoodsReceipt] (
    [Id] int NOT NULL IDENTITY,
    [Number] nvarchar(30) NOT NULL,
    [PurchaseOrderId] int NOT NULL,
    [ReceivedDate] date NOT NULL,
    [DeliveryNoteNumber] nvarchar(50) NULL,
    CONSTRAINT [PK_GoodsReceipt] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_GoodsReceipt_PurchaseOrder_PurchaseOrderId] FOREIGN KEY ([PurchaseOrderId]) REFERENCES [erp].[PurchaseOrder] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [ap].[MatchResult] (
    [Id] int NOT NULL IDENTITY,
    [DocumentId] int NOT NULL,
    [SupplierId] int NULL,
    [PurchaseOrderId] int NULL,
    [Decision] nvarchar(30) NOT NULL,
    [AmountAtStake] decimal(19,4) NOT NULL,
    [MatchedAt] datetimeoffset NOT NULL,
    [OverrideReason] nvarchar(1000) NULL,
    [ReviewedBy] nvarchar(200) NULL,
    [ReviewedAt] datetimeoffset NULL,
    [BankChangeVerified] bit NOT NULL,
    [PeriodEnd] datetime2 GENERATED ALWAYS AS ROW END HIDDEN NOT NULL,
    [PeriodStart] datetime2 GENERATED ALWAYS AS ROW START HIDDEN NOT NULL,
    CONSTRAINT [PK_MatchResult] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_MatchResult_Document_DocumentId] FOREIGN KEY ([DocumentId]) REFERENCES [ap].[Document] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_MatchResult_PurchaseOrder_PurchaseOrderId] FOREIGN KEY ([PurchaseOrderId]) REFERENCES [erp].[PurchaseOrder] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_MatchResult_Supplier_SupplierId] FOREIGN KEY ([SupplierId]) REFERENCES [erp].[Supplier] ([Id]) ON DELETE NO ACTION,
    PERIOD FOR SYSTEM_TIME([PeriodStart], [PeriodEnd])
) WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = [ap].[MatchResultHistory]));

CREATE TABLE [ap].[ItemAlias] (
    [Id] int NOT NULL IDENTITY,
    [SupplierItemId] int NOT NULL,
    [NormalisedDescription] nvarchar(300) NOT NULL,
    [CreatedBy] nvarchar(200) NOT NULL,
    [CreatedAt] datetimeoffset NOT NULL,
    CONSTRAINT [PK_ItemAlias] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_ItemAlias_SupplierItem_SupplierItemId] FOREIGN KEY ([SupplierItemId]) REFERENCES [erp].[SupplierItem] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [erp].[PurchaseOrderLine] (
    [Id] int NOT NULL IDENTITY,
    [PurchaseOrderId] int NOT NULL,
    [LineNumber] int NOT NULL,
    [SupplierItemId] int NOT NULL,
    [Description] nvarchar(300) NOT NULL,
    [Quantity] decimal(18,4) NOT NULL,
    [Unit] nvarchar(20) NOT NULL,
    [UnitPrice] decimal(19,4) NOT NULL,
    [VatRate] decimal(5,4) NOT NULL,
    [Status] nvarchar(30) NOT NULL,
    [RowVersion] rowversion NOT NULL,
    [PeriodEnd] datetime2 GENERATED ALWAYS AS ROW END HIDDEN NOT NULL,
    [PeriodStart] datetime2 GENERATED ALWAYS AS ROW START HIDDEN NOT NULL,
    CONSTRAINT [PK_PurchaseOrderLine] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_PurchaseOrderLine_PurchaseOrder_PurchaseOrderId] FOREIGN KEY ([PurchaseOrderId]) REFERENCES [erp].[PurchaseOrder] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_PurchaseOrderLine_SupplierItem_SupplierItemId] FOREIGN KEY ([SupplierItemId]) REFERENCES [erp].[SupplierItem] ([Id]) ON DELETE NO ACTION,
    PERIOD FOR SYSTEM_TIME([PeriodStart], [PeriodEnd])
) WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = [erp].[PurchaseOrderLineHistory]));

CREATE TABLE [ap].[FieldCorrection] (
    [Id] int NOT NULL IDENTITY,
    [ExtractedDocumentId] int NOT NULL,
    [ExtractedLineId] int NULL,
    [FieldName] nvarchar(100) NOT NULL,
    [ExtractedValue] nvarchar(500) NULL,
    [ExtractedConfidence] float NULL,
    [CorrectedValue] nvarchar(500) NULL,
    [Reviewer] nvarchar(200) NOT NULL,
    [CorrectedAt] datetimeoffset NOT NULL,
    CONSTRAINT [PK_FieldCorrection] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_FieldCorrection_ExtractedDocument_ExtractedDocumentId] FOREIGN KEY ([ExtractedDocumentId]) REFERENCES [ap].[ExtractedDocument] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_FieldCorrection_ExtractedLine_ExtractedLineId] FOREIGN KEY ([ExtractedLineId]) REFERENCES [ap].[ExtractedLine] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [erp].[GoodsReceiptLine] (
    [Id] int NOT NULL IDENTITY,
    [GoodsReceiptId] int NOT NULL,
    [PurchaseOrderLineId] int NOT NULL,
    [Quantity] decimal(18,4) NOT NULL,
    CONSTRAINT [PK_GoodsReceiptLine] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_GoodsReceiptLine_GoodsReceipt_GoodsReceiptId] FOREIGN KEY ([GoodsReceiptId]) REFERENCES [erp].[GoodsReceipt] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_GoodsReceiptLine_PurchaseOrderLine_PurchaseOrderLineId] FOREIGN KEY ([PurchaseOrderLineId]) REFERENCES [erp].[PurchaseOrderLine] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [ap].[LineMatch] (
    [Id] int NOT NULL IDENTITY,
    [MatchResultId] int NOT NULL,
    [ExtractedLineId] int NOT NULL,
    [PurchaseOrderLineId] int NULL,
    [Method] nvarchar(30) NOT NULL,
    [Confidence] float NOT NULL,
    [ConvertedQuantity] decimal(18,4) NULL,
    CONSTRAINT [PK_LineMatch] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_LineMatch_ExtractedLine_ExtractedLineId] FOREIGN KEY ([ExtractedLineId]) REFERENCES [ap].[ExtractedLine] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_LineMatch_MatchResult_MatchResultId] FOREIGN KEY ([MatchResultId]) REFERENCES [ap].[MatchResult] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_LineMatch_PurchaseOrderLine_PurchaseOrderLineId] FOREIGN KEY ([PurchaseOrderLineId]) REFERENCES [erp].[PurchaseOrderLine] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [ap].[Discrepancy] (
    [Id] int NOT NULL IDENTITY,
    [MatchResultId] int NOT NULL,
    [Code] nvarchar(30) NOT NULL,
    [Severity] nvarchar(30) NOT NULL,
    [LineMatchId] int NULL,
    [ExpectedValue] nvarchar(200) NULL,
    [ActualValue] nvarchar(200) NULL,
    [AmountAtStake] decimal(19,4) NOT NULL,
    CONSTRAINT [PK_Discrepancy] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Discrepancy_LineMatch_LineMatchId] FOREIGN KEY ([LineMatchId]) REFERENCES [ap].[LineMatch] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_Discrepancy_MatchResult_MatchResultId] FOREIGN KEY ([MatchResultId]) REFERENCES [ap].[MatchResult] ([Id]) ON DELETE CASCADE
);

CREATE TABLE [ap].[LineMatchGoodsReceiptLine] (
    [GoodsReceiptLineId] int NOT NULL,
    [LineMatchId] int NOT NULL,
    CONSTRAINT [PK_LineMatchGoodsReceiptLine] PRIMARY KEY ([GoodsReceiptLineId], [LineMatchId]),
    CONSTRAINT [FK_LineMatchGoodsReceiptLine_GoodsReceiptLine_GoodsReceiptLineId] FOREIGN KEY ([GoodsReceiptLineId]) REFERENCES [erp].[GoodsReceiptLine] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_LineMatchGoodsReceiptLine_LineMatch_LineMatchId] FOREIGN KEY ([LineMatchId]) REFERENCES [ap].[LineMatch] ([Id]) ON DELETE CASCADE
);

CREATE INDEX [IX_Discrepancy_LineMatchId] ON [ap].[Discrepancy] ([LineMatchId]);

CREATE INDEX [IX_Discrepancy_MatchResultId] ON [ap].[Discrepancy] ([MatchResultId]);

CREATE UNIQUE INDEX [IX_Document_ContentHash] ON [ap].[Document] ([ContentHash]);

CREATE INDEX [IX_Document_Status] ON [ap].[Document] ([Status]);

CREATE UNIQUE INDEX [IX_ExtractedDocument_DocumentId] ON [ap].[ExtractedDocument] ([DocumentId]);

CREATE INDEX [IX_ExtractedDocument_SupplierVatNumber_DocumentNumber] ON [ap].[ExtractedDocument] ([SupplierVatNumber], [DocumentNumber]);

CREATE INDEX [IX_ExtractedLine_ExtractedDocumentId] ON [ap].[ExtractedLine] ([ExtractedDocumentId]);

CREATE INDEX [IX_FieldCorrection_ExtractedDocumentId] ON [ap].[FieldCorrection] ([ExtractedDocumentId]);

CREATE INDEX [IX_FieldCorrection_ExtractedLineId] ON [ap].[FieldCorrection] ([ExtractedLineId]);

CREATE UNIQUE INDEX [IX_GoodsReceipt_Number] ON [erp].[GoodsReceipt] ([Number]);

CREATE INDEX [IX_GoodsReceipt_PurchaseOrderId] ON [erp].[GoodsReceipt] ([PurchaseOrderId]);

CREATE INDEX [IX_GoodsReceiptLine_GoodsReceiptId] ON [erp].[GoodsReceiptLine] ([GoodsReceiptId]);

CREATE INDEX [IX_GoodsReceiptLine_PurchaseOrderLineId] ON [erp].[GoodsReceiptLine] ([PurchaseOrderLineId]);

CREATE UNIQUE INDEX [IX_Item_Code] ON [erp].[Item] ([Code]);

CREATE UNIQUE INDEX [IX_ItemAlias_SupplierItemId_NormalisedDescription] ON [ap].[ItemAlias] ([SupplierItemId], [NormalisedDescription]);

CREATE INDEX [IX_LineMatch_ExtractedLineId] ON [ap].[LineMatch] ([ExtractedLineId]);

CREATE INDEX [IX_LineMatch_MatchResultId] ON [ap].[LineMatch] ([MatchResultId]);

CREATE INDEX [IX_LineMatch_PurchaseOrderLineId] ON [ap].[LineMatch] ([PurchaseOrderLineId]);

CREATE INDEX [IX_LineMatchGoodsReceiptLine_LineMatchId] ON [ap].[LineMatchGoodsReceiptLine] ([LineMatchId]);

CREATE INDEX [IX_MatchResult_Decision] ON [ap].[MatchResult] ([Decision]);

CREATE UNIQUE INDEX [IX_MatchResult_DocumentId] ON [ap].[MatchResult] ([DocumentId]);

CREATE INDEX [IX_MatchResult_PurchaseOrderId] ON [ap].[MatchResult] ([PurchaseOrderId]);

CREATE INDEX [IX_MatchResult_SupplierId] ON [ap].[MatchResult] ([SupplierId]);

CREATE UNIQUE INDEX [IX_PurchaseOrder_Number] ON [erp].[PurchaseOrder] ([Number]);

CREATE INDEX [IX_PurchaseOrder_SupplierId] ON [erp].[PurchaseOrder] ([SupplierId]);

CREATE UNIQUE INDEX [IX_PurchaseOrderLine_PurchaseOrderId_LineNumber] ON [erp].[PurchaseOrderLine] ([PurchaseOrderId], [LineNumber]);

CREATE INDEX [IX_PurchaseOrderLine_SupplierItemId] ON [erp].[PurchaseOrderLine] ([SupplierItemId]);

CREATE UNIQUE INDEX [IX_Supplier_VatNumber] ON [erp].[Supplier] ([VatNumber]);

CREATE INDEX [IX_SupplierItem_ItemId] ON [erp].[SupplierItem] ([ItemId]);

CREATE UNIQUE INDEX [IX_SupplierItem_SupplierId_SupplierItemCode] ON [erp].[SupplierItem] ([SupplierId], [SupplierItemCode]);

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261010195531_InitialSchema', N'10.0.12');

COMMIT;
GO


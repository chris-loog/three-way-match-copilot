# ER diagram

Two SQL schemas. `erp.` is the mock ERP (purchasing and warehouse). `ap.` is the application (documents, extraction, matching, review).
Tables marked *temporal* keep every earlier version of a row in a history table, which is the audit trail.

```mermaid
erDiagram
    %% ---------- erp ----------
    SUPPLIER ||--o{ SUPPLIER_ITEM : sells
    ITEM ||--o{ SUPPLIER_ITEM : "sold as"
    SUPPLIER ||--o{ PURCHASE_ORDER : receives
    PURCHASE_ORDER ||--|{ PURCHASE_ORDER_LINE : contains
    SUPPLIER_ITEM ||--o{ PURCHASE_ORDER_LINE : "ordered as"
    PURCHASE_ORDER ||--o{ GOODS_RECEIPT : "delivered by"
    GOODS_RECEIPT ||--|{ GOODS_RECEIPT_LINE : contains
    PURCHASE_ORDER_LINE ||--o{ GOODS_RECEIPT_LINE : "received in"

    %% ---------- ap ----------
    DOCUMENT ||--o| EXTRACTED_DOCUMENT : "read into"
    EXTRACTED_DOCUMENT ||--o{ EXTRACTED_LINE : has
    DOCUMENT ||--o| MATCH_RESULT : "decided by"
    MATCH_RESULT ||--o{ LINE_MATCH : has
    MATCH_RESULT ||--o{ DISCREPANCY : has
    EXTRACTED_LINE ||--o{ LINE_MATCH : "matched in"
    PURCHASE_ORDER_LINE ||--o{ LINE_MATCH : "matched to"
    LINE_MATCH }o--o{ GOODS_RECEIPT_LINE : "compared with"
    LINE_MATCH ||--o{ DISCREPANCY : "about"
    EXTRACTED_DOCUMENT ||--o{ FIELD_CORRECTION : "corrected by"
    SUPPLIER_ITEM ||--o{ ITEM_ALIAS : "known as"
    SUPPLIER ||--o{ MATCH_RESULT : "for"
    PURCHASE_ORDER ||--o{ MATCH_RESULT : "against"

    SUPPLIER {
        int Id PK
        string Name
        string Category
        string VatNumber UK
        string RegistryCode
        string Iban "master data"
        string DocumentCulture "et-EE, en-GB"
        decimal PriceTolerancePercent "optional override"
        decimal QuantityTolerancePercent "optional override"
    }
    ITEM {
        int Id PK
        string Code UK
        string Name
        string BaseUnit "m3, pcs"
    }
    SUPPLIER_ITEM {
        int Id PK
        int SupplierId FK
        int ItemId FK
        string SupplierItemCode "unique per supplier"
        string SupplierUnit
        decimal ConversionFactorToBase
    }
    PURCHASE_ORDER {
        int Id PK
        string Number UK
        int SupplierId FK
        date OrderDate
        string Currency
    }
    PURCHASE_ORDER_LINE {
        int Id PK "temporal"
        int PurchaseOrderId FK
        int LineNumber
        int SupplierItemId FK
        decimal Quantity "supplier unit"
        decimal UnitPrice
        decimal VatRate
        string Status "Open, PartiallyReceived, Received, Closed"
        rowversion RowVersion "optimistic concurrency"
    }
    GOODS_RECEIPT {
        int Id PK
        string Number UK
        int PurchaseOrderId FK
        date ReceivedDate
        string DeliveryNoteNumber
    }
    GOODS_RECEIPT_LINE {
        int Id PK
        int GoodsReceiptId FK
        int PurchaseOrderLineId FK
        decimal Quantity
    }
    DOCUMENT {
        int Id PK "temporal"
        string BlobPath
        string Type "Invoice, DeliveryNote"
        string Status
        string ContentHash UK "SHA-256"
        string ErrorMessage
    }
    EXTRACTED_DOCUMENT {
        int Id PK
        int DocumentId FK
        string ModelId
        string SupplierVatNumber
        string DocumentNumber
        string PurchaseOrderReference
        decimal NetTotal
        decimal VatTotal
        decimal GrossTotal
        string Iban
        double MinKeyFieldConfidence
        json FieldConfidenceJson
        json RawJson
    }
    EXTRACTED_LINE {
        int Id PK
        int ExtractedDocumentId FK
        string SupplierItemCode
        string Description
        decimal Quantity
        string Unit
        decimal UnitPrice
        double Confidence
    }
    MATCH_RESULT {
        int Id PK "temporal"
        int DocumentId FK
        int SupplierId FK
        int PurchaseOrderId FK
        string Decision "AutoApproved, OnHold, NeedsReview, Rejected"
        decimal AmountAtStake
        string OverrideReason
        string ReviewedBy
        bool BankChangeVerified
    }
    LINE_MATCH {
        int Id PK
        int MatchResultId FK
        int ExtractedLineId FK
        int PurchaseOrderLineId FK
        string Method "SupplierItemCode, ItemCode, LearnedAlias, FuzzyDescription"
        double Confidence
        decimal ConvertedQuantity "base unit"
    }
    DISCREPANCY {
        int Id PK
        int MatchResultId FK
        int LineMatchId FK
        string Code
        string Severity "Info, Warning, Block"
        string ExpectedValue
        string ActualValue
        decimal AmountAtStake
    }
    FIELD_CORRECTION {
        int Id PK
        int ExtractedDocumentId FK
        int ExtractedLineId FK
        string FieldName
        string ExtractedValue
        string CorrectedValue
        string Reviewer
    }
    ITEM_ALIAS {
        int Id PK
        int SupplierItemId FK
        string NormalisedDescription "unique per supplier item"
    }
```

## Notes

- **Units.** `Item.BaseUnit` is what the warehouse counts in. A PO line is stored in the supplier's unit, exactly as on the supplier's paperwork. The matching engine multiplies by `SupplierItem.ConversionFactorToBase` before it compares anything.
- **Partial deliveries.** A PO line has many `GoodsReceiptLine` rows, one per delivery. Received quantity is their sum.
- **Audit trail.** `Document`, `MatchResult` and `PurchaseOrderLine` are temporal tables. `SELECT ... FOR SYSTEM_TIME ALL` shows every earlier version and when it changed.
- **Learned aliases** live in `ap.ItemAlias` and point at `erp.SupplierItem`, so the app never writes to the ERP tables.
- **Deletes.** Foreign keys do not cascade except where a parent owns its children (PO → lines, receipt → lines, document → extraction and match result, match result → line matches and findings).

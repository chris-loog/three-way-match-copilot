using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ThreeWayMatch.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "ap");

            migrationBuilder.EnsureSchema(
                name: "erp");

            migrationBuilder.CreateTable(
                name: "Document",
                schema: "ap",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BlobPath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Type = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ContentHash = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    ErrorMessage = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PeriodEnd = table.Column<DateTime>(type: "datetime2", nullable: false)
                        .Annotation("SqlServer:TemporalIsPeriodEndColumn", true),
                    PeriodStart = table.Column<DateTime>(type: "datetime2", nullable: false)
                        .Annotation("SqlServer:TemporalIsPeriodStartColumn", true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Document", x => x.Id);
                })
                .Annotation("SqlServer:IsTemporal", true)
                .Annotation("SqlServer:TemporalHistoryTableName", "DocumentHistory")
                .Annotation("SqlServer:TemporalHistoryTableSchema", "ap")
                .Annotation("SqlServer:TemporalPeriodEndColumnName", "PeriodEnd")
                .Annotation("SqlServer:TemporalPeriodStartColumnName", "PeriodStart");

            migrationBuilder.CreateTable(
                name: "Item",
                schema: "erp",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    BaseUnit = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Item", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Supplier",
                schema: "erp",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Category = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    VatNumber = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RegistryCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Iban = table.Column<string>(type: "nvarchar(34)", maxLength: 34, nullable: false),
                    DocumentCulture = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    PriceTolerancePercent = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: true),
                    QuantityTolerancePercent = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Supplier", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExtractedDocument",
                schema: "ap",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DocumentId = table.Column<int>(type: "int", nullable: false),
                    ModelId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SupplierName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    SupplierVatNumber = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    DocumentNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    PurchaseOrderReference = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    DocumentDate = table.Column<DateOnly>(type: "date", nullable: true),
                    NetTotal = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    VatTotal = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    GrossTotal = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    Currency = table.Column<string>(type: "nchar(3)", fixedLength: true, maxLength: 3, nullable: true),
                    Iban = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    MinKeyFieldConfidence = table.Column<double>(type: "float", nullable: true),
                    FieldConfidenceJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RawJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ExtractedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExtractedDocument", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExtractedDocument_Document_DocumentId",
                        column: x => x.DocumentId,
                        principalSchema: "ap",
                        principalTable: "Document",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseOrder",
                schema: "erp",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    SupplierId = table.Column<int>(type: "int", nullable: false),
                    OrderDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Currency = table.Column<string>(type: "nchar(3)", fixedLength: true, maxLength: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseOrder", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseOrder_Supplier_SupplierId",
                        column: x => x.SupplierId,
                        principalSchema: "erp",
                        principalTable: "Supplier",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SupplierItem",
                schema: "erp",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SupplierId = table.Column<int>(type: "int", nullable: false),
                    ItemId = table.Column<int>(type: "int", nullable: false),
                    SupplierItemCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    SupplierDescription = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    SupplierUnit = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ConversionFactorToBase = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplierItem", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupplierItem_Item_ItemId",
                        column: x => x.ItemId,
                        principalSchema: "erp",
                        principalTable: "Item",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierItem_Supplier_SupplierId",
                        column: x => x.SupplierId,
                        principalSchema: "erp",
                        principalTable: "Supplier",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ExtractedLine",
                schema: "ap",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ExtractedDocumentId = table.Column<int>(type: "int", nullable: false),
                    LineNumber = table.Column<int>(type: "int", nullable: false),
                    SupplierItemCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    Unit = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    UnitPrice = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    VatRate = table.Column<decimal>(type: "decimal(5,4)", precision: 5, scale: 4, nullable: true),
                    LineTotal = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    Confidence = table.Column<double>(type: "float", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExtractedLine", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExtractedLine_ExtractedDocument_ExtractedDocumentId",
                        column: x => x.ExtractedDocumentId,
                        principalSchema: "ap",
                        principalTable: "ExtractedDocument",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GoodsReceipt",
                schema: "erp",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    PurchaseOrderId = table.Column<int>(type: "int", nullable: false),
                    ReceivedDate = table.Column<DateOnly>(type: "date", nullable: false),
                    DeliveryNoteNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GoodsReceipt", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GoodsReceipt_PurchaseOrder_PurchaseOrderId",
                        column: x => x.PurchaseOrderId,
                        principalSchema: "erp",
                        principalTable: "PurchaseOrder",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MatchResult",
                schema: "ap",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DocumentId = table.Column<int>(type: "int", nullable: false),
                    SupplierId = table.Column<int>(type: "int", nullable: true),
                    PurchaseOrderId = table.Column<int>(type: "int", nullable: true),
                    Decision = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    AmountAtStake = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    MatchedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    OverrideReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ReviewedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    BankChangeVerified = table.Column<bool>(type: "bit", nullable: false),
                    PeriodEnd = table.Column<DateTime>(type: "datetime2", nullable: false)
                        .Annotation("SqlServer:TemporalIsPeriodEndColumn", true),
                    PeriodStart = table.Column<DateTime>(type: "datetime2", nullable: false)
                        .Annotation("SqlServer:TemporalIsPeriodStartColumn", true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchResult", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MatchResult_Document_DocumentId",
                        column: x => x.DocumentId,
                        principalSchema: "ap",
                        principalTable: "Document",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MatchResult_PurchaseOrder_PurchaseOrderId",
                        column: x => x.PurchaseOrderId,
                        principalSchema: "erp",
                        principalTable: "PurchaseOrder",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MatchResult_Supplier_SupplierId",
                        column: x => x.SupplierId,
                        principalSchema: "erp",
                        principalTable: "Supplier",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("SqlServer:IsTemporal", true)
                .Annotation("SqlServer:TemporalHistoryTableName", "MatchResultHistory")
                .Annotation("SqlServer:TemporalHistoryTableSchema", "ap")
                .Annotation("SqlServer:TemporalPeriodEndColumnName", "PeriodEnd")
                .Annotation("SqlServer:TemporalPeriodStartColumnName", "PeriodStart");

            migrationBuilder.CreateTable(
                name: "ItemAlias",
                schema: "ap",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SupplierItemId = table.Column<int>(type: "int", nullable: false),
                    NormalisedDescription = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemAlias", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ItemAlias_SupplierItem_SupplierItemId",
                        column: x => x.SupplierItemId,
                        principalSchema: "erp",
                        principalTable: "SupplierItem",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseOrderLine",
                schema: "erp",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PurchaseOrderId = table.Column<int>(type: "int", nullable: false),
                    LineNumber = table.Column<int>(type: "int", nullable: false),
                    SupplierItemId = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    VatRate = table.Column<decimal>(type: "decimal(5,4)", precision: 5, scale: 4, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    PeriodEnd = table.Column<DateTime>(type: "datetime2", nullable: false)
                        .Annotation("SqlServer:TemporalIsPeriodEndColumn", true),
                    PeriodStart = table.Column<DateTime>(type: "datetime2", nullable: false)
                        .Annotation("SqlServer:TemporalIsPeriodStartColumn", true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseOrderLine", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseOrderLine_PurchaseOrder_PurchaseOrderId",
                        column: x => x.PurchaseOrderId,
                        principalSchema: "erp",
                        principalTable: "PurchaseOrder",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PurchaseOrderLine_SupplierItem_SupplierItemId",
                        column: x => x.SupplierItemId,
                        principalSchema: "erp",
                        principalTable: "SupplierItem",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("SqlServer:IsTemporal", true)
                .Annotation("SqlServer:TemporalHistoryTableName", "PurchaseOrderLineHistory")
                .Annotation("SqlServer:TemporalHistoryTableSchema", "erp")
                .Annotation("SqlServer:TemporalPeriodEndColumnName", "PeriodEnd")
                .Annotation("SqlServer:TemporalPeriodStartColumnName", "PeriodStart");

            migrationBuilder.CreateTable(
                name: "FieldCorrection",
                schema: "ap",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ExtractedDocumentId = table.Column<int>(type: "int", nullable: false),
                    ExtractedLineId = table.Column<int>(type: "int", nullable: true),
                    FieldName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ExtractedValue = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ExtractedConfidence = table.Column<double>(type: "float", nullable: true),
                    CorrectedValue = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Reviewer = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CorrectedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FieldCorrection", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FieldCorrection_ExtractedDocument_ExtractedDocumentId",
                        column: x => x.ExtractedDocumentId,
                        principalSchema: "ap",
                        principalTable: "ExtractedDocument",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FieldCorrection_ExtractedLine_ExtractedLineId",
                        column: x => x.ExtractedLineId,
                        principalSchema: "ap",
                        principalTable: "ExtractedLine",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "GoodsReceiptLine",
                schema: "erp",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    GoodsReceiptId = table.Column<int>(type: "int", nullable: false),
                    PurchaseOrderLineId = table.Column<int>(type: "int", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GoodsReceiptLine", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GoodsReceiptLine_GoodsReceipt_GoodsReceiptId",
                        column: x => x.GoodsReceiptId,
                        principalSchema: "erp",
                        principalTable: "GoodsReceipt",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GoodsReceiptLine_PurchaseOrderLine_PurchaseOrderLineId",
                        column: x => x.PurchaseOrderLineId,
                        principalSchema: "erp",
                        principalTable: "PurchaseOrderLine",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LineMatch",
                schema: "ap",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MatchResultId = table.Column<int>(type: "int", nullable: false),
                    ExtractedLineId = table.Column<int>(type: "int", nullable: false),
                    PurchaseOrderLineId = table.Column<int>(type: "int", nullable: true),
                    Method = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Confidence = table.Column<double>(type: "float", nullable: false),
                    ConvertedQuantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LineMatch", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LineMatch_ExtractedLine_ExtractedLineId",
                        column: x => x.ExtractedLineId,
                        principalSchema: "ap",
                        principalTable: "ExtractedLine",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LineMatch_MatchResult_MatchResultId",
                        column: x => x.MatchResultId,
                        principalSchema: "ap",
                        principalTable: "MatchResult",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LineMatch_PurchaseOrderLine_PurchaseOrderLineId",
                        column: x => x.PurchaseOrderLineId,
                        principalSchema: "erp",
                        principalTable: "PurchaseOrderLine",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Discrepancy",
                schema: "ap",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MatchResultId = table.Column<int>(type: "int", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Severity = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    LineMatchId = table.Column<int>(type: "int", nullable: true),
                    ExpectedValue = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ActualValue = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    AmountAtStake = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Discrepancy", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Discrepancy_LineMatch_LineMatchId",
                        column: x => x.LineMatchId,
                        principalSchema: "ap",
                        principalTable: "LineMatch",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Discrepancy_MatchResult_MatchResultId",
                        column: x => x.MatchResultId,
                        principalSchema: "ap",
                        principalTable: "MatchResult",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LineMatchGoodsReceiptLine",
                schema: "ap",
                columns: table => new
                {
                    GoodsReceiptLineId = table.Column<int>(type: "int", nullable: false),
                    LineMatchId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LineMatchGoodsReceiptLine", x => new { x.GoodsReceiptLineId, x.LineMatchId });
                    table.ForeignKey(
                        name: "FK_LineMatchGoodsReceiptLine_GoodsReceiptLine_GoodsReceiptLineId",
                        column: x => x.GoodsReceiptLineId,
                        principalSchema: "erp",
                        principalTable: "GoodsReceiptLine",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LineMatchGoodsReceiptLine_LineMatch_LineMatchId",
                        column: x => x.LineMatchId,
                        principalSchema: "ap",
                        principalTable: "LineMatch",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Discrepancy_LineMatchId",
                schema: "ap",
                table: "Discrepancy",
                column: "LineMatchId");

            migrationBuilder.CreateIndex(
                name: "IX_Discrepancy_MatchResultId",
                schema: "ap",
                table: "Discrepancy",
                column: "MatchResultId");

            migrationBuilder.CreateIndex(
                name: "IX_Document_ContentHash",
                schema: "ap",
                table: "Document",
                column: "ContentHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Document_Status",
                schema: "ap",
                table: "Document",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_ExtractedDocument_DocumentId",
                schema: "ap",
                table: "ExtractedDocument",
                column: "DocumentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExtractedDocument_SupplierVatNumber_DocumentNumber",
                schema: "ap",
                table: "ExtractedDocument",
                columns: new[] { "SupplierVatNumber", "DocumentNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_ExtractedLine_ExtractedDocumentId",
                schema: "ap",
                table: "ExtractedLine",
                column: "ExtractedDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_FieldCorrection_ExtractedDocumentId",
                schema: "ap",
                table: "FieldCorrection",
                column: "ExtractedDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_FieldCorrection_ExtractedLineId",
                schema: "ap",
                table: "FieldCorrection",
                column: "ExtractedLineId");

            migrationBuilder.CreateIndex(
                name: "IX_GoodsReceipt_Number",
                schema: "erp",
                table: "GoodsReceipt",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GoodsReceipt_PurchaseOrderId",
                schema: "erp",
                table: "GoodsReceipt",
                column: "PurchaseOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_GoodsReceiptLine_GoodsReceiptId",
                schema: "erp",
                table: "GoodsReceiptLine",
                column: "GoodsReceiptId");

            migrationBuilder.CreateIndex(
                name: "IX_GoodsReceiptLine_PurchaseOrderLineId",
                schema: "erp",
                table: "GoodsReceiptLine",
                column: "PurchaseOrderLineId");

            migrationBuilder.CreateIndex(
                name: "IX_Item_Code",
                schema: "erp",
                table: "Item",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ItemAlias_SupplierItemId_NormalisedDescription",
                schema: "ap",
                table: "ItemAlias",
                columns: new[] { "SupplierItemId", "NormalisedDescription" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LineMatch_ExtractedLineId",
                schema: "ap",
                table: "LineMatch",
                column: "ExtractedLineId");

            migrationBuilder.CreateIndex(
                name: "IX_LineMatch_MatchResultId",
                schema: "ap",
                table: "LineMatch",
                column: "MatchResultId");

            migrationBuilder.CreateIndex(
                name: "IX_LineMatch_PurchaseOrderLineId",
                schema: "ap",
                table: "LineMatch",
                column: "PurchaseOrderLineId");

            migrationBuilder.CreateIndex(
                name: "IX_LineMatchGoodsReceiptLine_LineMatchId",
                schema: "ap",
                table: "LineMatchGoodsReceiptLine",
                column: "LineMatchId");

            migrationBuilder.CreateIndex(
                name: "IX_MatchResult_Decision",
                schema: "ap",
                table: "MatchResult",
                column: "Decision");

            migrationBuilder.CreateIndex(
                name: "IX_MatchResult_DocumentId",
                schema: "ap",
                table: "MatchResult",
                column: "DocumentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MatchResult_PurchaseOrderId",
                schema: "ap",
                table: "MatchResult",
                column: "PurchaseOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_MatchResult_SupplierId",
                schema: "ap",
                table: "MatchResult",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrder_Number",
                schema: "erp",
                table: "PurchaseOrder",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrder_SupplierId",
                schema: "erp",
                table: "PurchaseOrder",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrderLine_PurchaseOrderId_LineNumber",
                schema: "erp",
                table: "PurchaseOrderLine",
                columns: new[] { "PurchaseOrderId", "LineNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrderLine_SupplierItemId",
                schema: "erp",
                table: "PurchaseOrderLine",
                column: "SupplierItemId");

            migrationBuilder.CreateIndex(
                name: "IX_Supplier_VatNumber",
                schema: "erp",
                table: "Supplier",
                column: "VatNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupplierItem_ItemId",
                schema: "erp",
                table: "SupplierItem",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierItem_SupplierId_SupplierItemCode",
                schema: "erp",
                table: "SupplierItem",
                columns: new[] { "SupplierId", "SupplierItemCode" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Discrepancy",
                schema: "ap");

            migrationBuilder.DropTable(
                name: "FieldCorrection",
                schema: "ap");

            migrationBuilder.DropTable(
                name: "ItemAlias",
                schema: "ap");

            migrationBuilder.DropTable(
                name: "LineMatchGoodsReceiptLine",
                schema: "ap");

            migrationBuilder.DropTable(
                name: "GoodsReceiptLine",
                schema: "erp");

            migrationBuilder.DropTable(
                name: "LineMatch",
                schema: "ap");

            migrationBuilder.DropTable(
                name: "GoodsReceipt",
                schema: "erp");

            migrationBuilder.DropTable(
                name: "ExtractedLine",
                schema: "ap");

            migrationBuilder.DropTable(
                name: "MatchResult",
                schema: "ap")
                .Annotation("SqlServer:IsTemporal", true)
                .Annotation("SqlServer:TemporalHistoryTableName", "MatchResultHistory")
                .Annotation("SqlServer:TemporalHistoryTableSchema", "ap")
                .Annotation("SqlServer:TemporalPeriodEndColumnName", "PeriodEnd")
                .Annotation("SqlServer:TemporalPeriodStartColumnName", "PeriodStart");

            migrationBuilder.DropTable(
                name: "PurchaseOrderLine",
                schema: "erp")
                .Annotation("SqlServer:IsTemporal", true)
                .Annotation("SqlServer:TemporalHistoryTableName", "PurchaseOrderLineHistory")
                .Annotation("SqlServer:TemporalHistoryTableSchema", "erp")
                .Annotation("SqlServer:TemporalPeriodEndColumnName", "PeriodEnd")
                .Annotation("SqlServer:TemporalPeriodStartColumnName", "PeriodStart");

            migrationBuilder.DropTable(
                name: "ExtractedDocument",
                schema: "ap");

            migrationBuilder.DropTable(
                name: "PurchaseOrder",
                schema: "erp");

            migrationBuilder.DropTable(
                name: "SupplierItem",
                schema: "erp");

            migrationBuilder.DropTable(
                name: "Document",
                schema: "ap")
                .Annotation("SqlServer:IsTemporal", true)
                .Annotation("SqlServer:TemporalHistoryTableName", "DocumentHistory")
                .Annotation("SqlServer:TemporalHistoryTableSchema", "ap")
                .Annotation("SqlServer:TemporalPeriodEndColumnName", "PeriodEnd")
                .Annotation("SqlServer:TemporalPeriodStartColumnName", "PeriodStart");

            migrationBuilder.DropTable(
                name: "Item",
                schema: "erp");

            migrationBuilder.DropTable(
                name: "Supplier",
                schema: "erp");
        }
    }
}

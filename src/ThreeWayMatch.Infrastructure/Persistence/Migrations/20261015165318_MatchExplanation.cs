using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ThreeWayMatch.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MatchExplanation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MatchExplanation",
                schema: "ap",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DocumentId = table.Column<int>(type: "int", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ModelId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Summary = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    LikelyCause = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    SuggestedAction = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    DraftMessage = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    DraftLanguage = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    GuardNotes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    PromptTokens = table.Column<int>(type: "int", nullable: false),
                    CompletionTokens = table.Column<int>(type: "int", nullable: false),
                    GeneratedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchExplanation", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MatchExplanation_Document_DocumentId",
                        column: x => x.DocumentId,
                        principalSchema: "ap",
                        principalTable: "Document",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MatchExplanation_DocumentId",
                schema: "ap",
                table: "MatchExplanation",
                column: "DocumentId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MatchExplanation",
                schema: "ap");
        }
    }
}

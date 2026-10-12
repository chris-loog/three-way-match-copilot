using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ThreeWayMatch.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DocumentUniqueByBlobPath : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Document_ContentHash",
                schema: "ap",
                table: "Document");

            migrationBuilder.CreateIndex(
                name: "IX_Document_BlobPath",
                schema: "ap",
                table: "Document",
                column: "BlobPath",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Document_ContentHash",
                schema: "ap",
                table: "Document",
                column: "ContentHash");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Document_BlobPath",
                schema: "ap",
                table: "Document");

            migrationBuilder.DropIndex(
                name: "IX_Document_ContentHash",
                schema: "ap",
                table: "Document");

            migrationBuilder.CreateIndex(
                name: "IX_Document_ContentHash",
                schema: "ap",
                table: "Document",
                column: "ContentHash",
                unique: true);
        }
    }
}

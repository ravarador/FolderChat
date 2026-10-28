using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FolderChat.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTokenDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "CachedInputTokens",
                table: "Messages",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ContextTokens",
                table: "Messages",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ReasoningTokens",
                table: "Messages",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "SummaryInputTokens",
                table: "KnowledgeFolders",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "SummaryOutputTokens",
                table: "KnowledgeFolders",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CachedInputTokens",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "ContextTokens",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "ReasoningTokens",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "SummaryInputTokens",
                table: "KnowledgeFolders");

            migrationBuilder.DropColumn(
                name: "SummaryOutputTokens",
                table: "KnowledgeFolders");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FolderChat.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddKnowledgeFolders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Sources",
                table: "Messages",
                type: "TEXT",
                nullable: true);

            // Messages from before this version have no sources: an empty JSON list, not NULL.
            migrationBuilder.Sql("UPDATE Messages SET Sources = '[]' WHERE Sources IS NULL;");

            migrationBuilder.AddColumn<string>(
                name: "Effort",
                table: "Conversations",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "Standard");

            migrationBuilder.AddColumn<string>(
                name: "FolderIds",
                table: "Conversations",
                type: "TEXT",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.CreateTable(
                name: "KnowledgeFolders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Path = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastScannedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ScanError = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    SummariesEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    SummaryProviderId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SummaryModelId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KnowledgeFolders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "KnowledgeFiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    FolderId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RelativePath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    Size = table.Column<long>(type: "INTEGER", nullable: false),
                    LastWriteTimeUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Note = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    MediaType = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    PageCount = table.Column<int>(type: "INTEGER", nullable: true),
                    CharacterCount = table.Column<int>(type: "INTEGER", nullable: false),
                    IsTruncated = table.Column<bool>(type: "INTEGER", nullable: false),
                    Summary = table.Column<string>(type: "TEXT", nullable: true),
                    IndexedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KnowledgeFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_KnowledgeFiles_KnowledgeFolders_FolderId",
                        column: x => x.FolderId,
                        principalTable: "KnowledgeFolders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Passages",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FileId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Ordinal = table.Column<int>(type: "INTEGER", nullable: false),
                    PageNumber = table.Column<int>(type: "INTEGER", nullable: true),
                    StartOffset = table.Column<int>(type: "INTEGER", nullable: false),
                    Text = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Passages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Passages_KnowledgeFiles_FileId",
                        column: x => x.FileId,
                        principalTable: "KnowledgeFiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeFiles_FolderId_RelativePath",
                table: "KnowledgeFiles",
                columns: new[] { "FolderId", "RelativePath" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Passages_FileId_Ordinal",
                table: "Passages",
                columns: new[] { "FileId", "Ordinal" });

            // The full-text index over passages. It stores no text of its own ("external content"): triggers keep
            // it in step with the Passages table, including deletes that cascade from files and folders. Porter
            // stemming matches "costs" to "cost"; remove_diacritics matches "cafe" to "café".
            migrationBuilder.Sql("""
                CREATE VIRTUAL TABLE PassageSearch USING fts5(
                    Text,
                    content = 'Passages',
                    content_rowid = 'Id',
                    tokenize = 'porter unicode61 remove_diacritics 2');

                CREATE TRIGGER Passages_AfterInsert AFTER INSERT ON Passages BEGIN
                    INSERT INTO PassageSearch (rowid, Text) VALUES (new.Id, new.Text);
                END;

                CREATE TRIGGER Passages_AfterDelete AFTER DELETE ON Passages BEGIN
                    INSERT INTO PassageSearch (PassageSearch, rowid, Text) VALUES ('delete', old.Id, old.Text);
                END;

                CREATE TRIGGER Passages_AfterUpdate AFTER UPDATE ON Passages BEGIN
                    INSERT INTO PassageSearch (PassageSearch, rowid, Text) VALUES ('delete', old.Id, old.Text);
                    INSERT INTO PassageSearch (rowid, Text) VALUES (new.Id, new.Text);
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER Passages_AfterUpdate;
                DROP TRIGGER Passages_AfterDelete;
                DROP TRIGGER Passages_AfterInsert;
                DROP TABLE PassageSearch;
                """);

            migrationBuilder.DropTable(
                name: "Passages");

            migrationBuilder.DropTable(
                name: "KnowledgeFiles");

            migrationBuilder.DropTable(
                name: "KnowledgeFolders");

            migrationBuilder.DropColumn(
                name: "Sources",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "Effort",
                table: "Conversations");

            migrationBuilder.DropColumn(
                name: "FolderIds",
                table: "Conversations");
        }
    }
}

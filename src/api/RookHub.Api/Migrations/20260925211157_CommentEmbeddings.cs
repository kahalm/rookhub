using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class CommentEmbeddings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CommentEmbeddings",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    LibraryGameId = table.Column<int>(type: "int", nullable: false),
                    FromPly = table.Column<int>(type: "int", nullable: false),
                    ToPly = table.Column<int>(type: "int", nullable: false),
                    Text = table.Column<string>(type: "varchar(1600)", maxLength: 1600, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Vector = table.Column<byte[]>(type: "vector(512)", nullable: false),
                    Model = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommentEmbeddings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CommentEmbeddings_LibraryGames_LibraryGameId",
                        column: x => x.LibraryGameId,
                        principalTable: "LibraryGames",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_CommentEmbeddings_LibraryGameId",
                table: "CommentEmbeddings",
                column: "LibraryGameId");

            // Kosinus-Index für die semantische Suche (MariaDB ≥ 11.7). EF kann ihn nicht ausdrücken; die Abfrage muss
            // „ORDER BY VEC_DISTANCE_COSINE(`Vector`, @q) LIMIT n" (VECTOR ist ein Schlüsselwort — Spalte immer in Backticks) lauten, damit er benutzt wird.
            migrationBuilder.Sql("CREATE VECTOR INDEX IX_CommentEmbeddings_Vector ON `CommentEmbeddings` (`Vector`) M=8 DISTANCE=cosine;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CommentEmbeddings");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class EngineThroughputBuckets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EngineThroughputBuckets",
                columns: table => new
                {
                    EngineId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    BucketStart = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Runs = table.Column<int>(type: "int", nullable: false),
                    Done = table.Column<int>(type: "int", nullable: false),
                    Failed = table.Column<int>(type: "int", nullable: false),
                    Interrupted = table.Column<int>(type: "int", nullable: false),
                    Retried = table.Column<int>(type: "int", nullable: false),
                    Unavailable = table.Column<int>(type: "int", nullable: false),
                    Timeouts = table.Column<int>(type: "int", nullable: false),
                    Errors = table.Column<int>(type: "int", nullable: false),
                    RunSeconds = table.Column<long>(type: "bigint", nullable: false),
                    DoneJobSeconds = table.Column<long>(type: "bigint", nullable: false),
                    Nodes = table.Column<long>(type: "bigint", nullable: false),
                    NodeSeconds = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EngineThroughputBuckets", x => new { x.EngineId, x.BucketStart });
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_EngineThroughputBuckets_BucketStart",
                table: "EngineThroughputBuckets",
                column: "BucketStart");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EngineThroughputBuckets");
        }
    }
}

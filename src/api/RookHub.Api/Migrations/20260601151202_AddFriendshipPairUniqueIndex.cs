using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RookHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddFriendshipPairUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Etwaige Duplikat-Paare (vom direktionalen Bug erzeugt: A->B UND B->A)
            // VOR dem Unique-Index entfernen, sonst scheitert dessen Aufbau und die
            // Auto-Migration beim Start crasht. Es bleibt die jeweils aelteste Zeile (kleinste Id).
            migrationBuilder.Sql(@"
                DELETE f1 FROM Friendships f1
                INNER JOIN Friendships f2
                  ON LEAST(f1.RequesterId, f1.AddresseeId) = LEAST(f2.RequesterId, f2.AddresseeId)
                 AND GREATEST(f1.RequesterId, f1.AddresseeId) = GREATEST(f2.RequesterId, f2.AddresseeId)
                 AND f1.Id > f2.Id;");

            migrationBuilder.AddColumn<int>(
                name: "PairHigh",
                table: "Friendships",
                type: "int",
                nullable: false,
                computedColumnSql: "GREATEST(RequesterId, AddresseeId)",
                stored: true);

            migrationBuilder.AddColumn<int>(
                name: "PairLow",
                table: "Friendships",
                type: "int",
                nullable: false,
                computedColumnSql: "LEAST(RequesterId, AddresseeId)",
                stored: true);

            migrationBuilder.CreateIndex(
                name: "IX_Friendships_PairLow_PairHigh",
                table: "Friendships",
                columns: new[] { "PairLow", "PairHigh" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Friendships_PairLow_PairHigh",
                table: "Friendships");

            migrationBuilder.DropColumn(
                name: "PairHigh",
                table: "Friendships");

            migrationBuilder.DropColumn(
                name: "PairLow",
                table: "Friendships");
        }
    }
}

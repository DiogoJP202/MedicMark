using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChecklistPlantao.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SessaoAbertaUnicaPorSetor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Sessoes_Setor_Data_Aberta",
                table: "Sessoes");

            migrationBuilder.CreateIndex(
                name: "IX_Sessoes_Setor_Aberta",
                table: "Sessoes",
                column: "SectorId",
                unique: true,
                filter: "\"Status\" = 'Open'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Sessoes_Setor_Aberta",
                table: "Sessoes");

            migrationBuilder.CreateIndex(
                name: "IX_Sessoes_Setor_Data_Aberta",
                table: "Sessoes",
                columns: new[] { "SectorId", "ServiceDate" },
                unique: true,
                filter: "\"Status\" = 'Open'");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SF.Tecnologias.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RestricoesEmpresas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "CK_Empresas_Codigo_NaoVazio",
                table: "Empresas",
                sql: "\"Codigo\" <> ''");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Empresas_Nome_NaoVazio",
                table: "Empresas",
                sql: "\"Nome\" <> ''");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Empresas_Codigo_NaoVazio",
                table: "Empresas");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Empresas_Nome_NaoVazio",
                table: "Empresas");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SF.Tecnologias.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ConsolidarEstruturaEmpresarial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Permissoes_Perfis_PerfilId",
                table: "Permissoes");

            migrationBuilder.DropIndex(
                name: "IX_Permissoes_PerfilId",
                table: "Permissoes");

            migrationBuilder.DropIndex(
                name: "IX_Empresas_Nome",
                table: "Empresas");

            migrationBuilder.DropColumn(
                name: "PerfilId",
                table: "Permissoes");

            migrationBuilder.AddColumn<DateTime>(
                name: "AtualizadoEm",
                table: "UsuarioEmpresas",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CriadoEm",
                table: "UsuarioEmpresas",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<bool>(
                name: "Ativo",
                table: "Permissoes",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "AtualizadoEm",
                table: "Permissoes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CriadoEm",
                table: "Permissoes",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "AtualizadoEm",
                table: "Perfis",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CriadoEm",
                table: "Perfis",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "Codigo",
                table: "Empresas",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "PerfilPermissoes",
                columns: table => new
                {
                    PerfilId = table.Column<int>(type: "integer", nullable: false),
                    PermissoesId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PerfilPermissoes", x => new { x.PerfilId, x.PermissoesId });
                    table.ForeignKey(
                        name: "FK_PerfilPermissoes_Perfis_PerfilId",
                        column: x => x.PerfilId,
                        principalTable: "Perfis",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PerfilPermissoes_Permissoes_PermissoesId",
                        column: x => x.PermissoesId,
                        principalTable: "Permissoes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Perfis_Nome",
                table: "Perfis",
                column: "Nome",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Empresas_Codigo",
                table: "Empresas",
                column: "Codigo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Empresas_Nome",
                table: "Empresas",
                column: "Nome");

            migrationBuilder.CreateIndex(
                name: "IX_PerfilPermissoes_PermissoesId",
                table: "PerfilPermissoes",
                column: "PermissoesId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PerfilPermissoes");

            migrationBuilder.DropIndex(
                name: "IX_Perfis_Nome",
                table: "Perfis");

            migrationBuilder.DropIndex(
                name: "IX_Empresas_Codigo",
                table: "Empresas");

            migrationBuilder.DropIndex(
                name: "IX_Empresas_Nome",
                table: "Empresas");

            migrationBuilder.DropColumn(
                name: "AtualizadoEm",
                table: "UsuarioEmpresas");

            migrationBuilder.DropColumn(
                name: "CriadoEm",
                table: "UsuarioEmpresas");

            migrationBuilder.DropColumn(
                name: "Ativo",
                table: "Permissoes");

            migrationBuilder.DropColumn(
                name: "AtualizadoEm",
                table: "Permissoes");

            migrationBuilder.DropColumn(
                name: "CriadoEm",
                table: "Permissoes");

            migrationBuilder.DropColumn(
                name: "AtualizadoEm",
                table: "Perfis");

            migrationBuilder.DropColumn(
                name: "CriadoEm",
                table: "Perfis");

            migrationBuilder.DropColumn(
                name: "Codigo",
                table: "Empresas");

            migrationBuilder.AddColumn<int>(
                name: "PerfilId",
                table: "Permissoes",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Permissoes_PerfilId",
                table: "Permissoes",
                column: "PerfilId");

            migrationBuilder.CreateIndex(
                name: "IX_Empresas_Nome",
                table: "Empresas",
                column: "Nome",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Permissoes_Perfis_PerfilId",
                table: "Permissoes",
                column: "PerfilId",
                principalTable: "Perfis",
                principalColumn: "Id");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SF.Tecnologias.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AtualizarCategoriaClienteMesaCaixa : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Nome",
                table: "Mesas",
                newName: "Descricao");

            migrationBuilder.AddColumn<int>(
                name: "MesaId",
                table: "SessoesCaixa",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Capacidade",
                table: "Mesas",
                type: "integer",
                nullable: false,
                defaultValue: 4);

            migrationBuilder.AddColumn<string>(
                name: "Email",
                table: "Clientes",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Endereco",
                table: "Clientes",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Descricao",
                table: "Categorias",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Ordem",
                table: "Categorias",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_SessoesCaixa_MesaId",
                table: "SessoesCaixa",
                column: "MesaId");

            migrationBuilder.AddForeignKey(
                name: "FK_SessoesCaixa_Mesas_MesaId",
                table: "SessoesCaixa",
                column: "MesaId",
                principalTable: "Mesas",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SessoesCaixa_Mesas_MesaId",
                table: "SessoesCaixa");

            migrationBuilder.DropIndex(
                name: "IX_SessoesCaixa_MesaId",
                table: "SessoesCaixa");

            migrationBuilder.DropColumn(
                name: "MesaId",
                table: "SessoesCaixa");

            migrationBuilder.DropColumn(
                name: "Capacidade",
                table: "Mesas");

            migrationBuilder.DropColumn(
                name: "Email",
                table: "Clientes");

            migrationBuilder.DropColumn(
                name: "Endereco",
                table: "Clientes");

            migrationBuilder.DropColumn(
                name: "Descricao",
                table: "Categorias");

            migrationBuilder.DropColumn(
                name: "Ordem",
                table: "Categorias");

            migrationBuilder.RenameColumn(
                name: "Descricao",
                table: "Mesas",
                newName: "Nome");
        }
    }
}

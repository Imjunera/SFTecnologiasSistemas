using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace SF.Tecnologias.Infrastructure.Persistence
{
    /// <summary>
    /// A API usa EnsureCreated() no SQLite: se o banco JA existe, nenhuma tabela nova e criada
    /// (EF Core nunca migra schema de SQLite com EnsureCreated). Sem isso, qualquer evolucao
    /// de modelo (novas tabelas) quebraria instalacoes existentes com "no such table".
    ///
    /// Este bootstrap reaplica o script de criacao do modelo atual transformado em
    /// "IF NOT EXISTS": tabelas ja existentes sao ignoradas e apenas as ausentes (ex.:
    /// Vendas/VendaItens em bancos antigos) sao criadas, sempre identicas ao modelo do EF.
    /// </summary>
    public static class SqliteSchemaBootstrap
    {
        private static readonly Regex StatementSeparator = new(@";\s*\r?\n", RegexOptions.Compiled);

        public static async Task EnsureModelTablesAsync(AppDbContext context)
        {
            var database = context.Database;
            if (!database.IsSqlite())
                return;

            try
            {
                var script = database.GenerateCreateScript();
                foreach (var raw in StatementSeparator.Split(script))
                {
                    var statement = raw.Trim();
                    if (statement.Length == 0)
                        continue;

                    // CREATE TABLE "X" -> CREATE TABLE IF NOT EXISTS "X"
                    // CREATE UNIQUE INDEX "X" -> CREATE UNIQUE INDEX IF NOT EXISTS "X"
                    statement = Regex.Replace(
                        statement,
                        @"^CREATE (TABLE|UNIQUE INDEX|INDEX) ",
                        "CREATE $1 IF NOT EXISTS ");

                    await database.ExecuteSqlRawAsync(statement);
                }
            }
            catch (Exception ex)
            {
                // Nao derruba o startup: a API responde, mas deixa o problema explicito no log.
                Console.WriteLine("[SCHEMA] Falha ao garantir tabelas do modelo no SQLite: " + ex.Message);
            }
        }
    }
}

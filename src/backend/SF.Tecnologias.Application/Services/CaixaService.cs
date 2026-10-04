using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SF.Tecnologias.Application.DTOs;
using SF.Tecnologias.Domain;
using SF.Tecnologias.Infrastructure.Persistence;

namespace SF.Tecnologias.Application.Services
{
    public class CaixaService : ICaixaService
    {
        private readonly AppDbContext _context;
        private readonly ITenantProvider _tenantProvider;

        public CaixaService(AppDbContext context, ITenantProvider tenantProvider)
        {
            _context = context;
            _tenantProvider = tenantProvider;
        }

        private int GetCurrentTenantId()
        {
            var tenantId = _tenantProvider.GetTenantId();
            if (!tenantId.HasValue || tenantId.Value <= 0)
                throw new UnauthorizedAccessException("Contexto de empresa nao identificado.");
            return tenantId.Value;
        }

        private int GetCurrentUserId()
        {
            var userId = _tenantProvider.GetUsuarioId();
            if (!userId.HasValue || userId.Value <= 0)
                throw new UnauthorizedAccessException("Usuario nao identificado.");
            return userId.Value;
        }

        public async Task<SessaoCaixaDto?> ObterSessaoAbertaAsync()
        {
            var empresaId = GetCurrentTenantId();
            var sessao = await _context.SessoesCaixa
                .AsNoTracking()
                .Include(s => s.Usuario)
                .Include(s => s.Mesa)
                .FirstOrDefaultAsync(s => s.EmpresaId == empresaId && s.Status == StatusSessaoCaixa.Aberta);

            if (sessao == null) return null;

            var (quantidadeVendas, totalVendas) = await CalcularTotaisAsync(sessao.Id);

            return new SessaoCaixaDto
            {
                Id = sessao.Id,
                EmpresaId = sessao.EmpresaId,
                UsuarioId = sessao.UsuarioId,
                UsuarioNome = sessao.Usuario.Nome,
                MesaId = sessao.MesaId,
                MesaNumero = sessao.Mesa?.Numero,
                DataAbertura = sessao.DataAbertura,
                ValorInicial = sessao.ValorInicial,
                DataFechamento = sessao.DataFechamento,
                ValorFechamento = sessao.ValorFechamento,
                Status = sessao.Status.ToString(),
                TotalVendas = totalVendas,
                QuantidadeVendas = quantidadeVendas
            };
        }

        public async Task<ResumoCaixaDto?> ObterResumoAsync()
        {
            var empresaId = GetCurrentTenantId();

            var sessao = await _context.SessoesCaixa
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.EmpresaId == empresaId && s.Status == StatusSessaoCaixa.Aberta);

            if (sessao == null) return null;

            var (quantidadeVendas, totalVendas) = await CalcularTotaisAsync(sessao.Id);

            var porForma = (await _context.Vendas
                .AsNoTracking()
                .Where(v => v.SessaoCaixaId == sessao.Id && v.Status == StatusVenda.Concluida)
                .Select(v => new { v.FormaPagamento, v.ValorTotal })
                .ToListAsync())
                .GroupBy(v => v.FormaPagamento)
                .Select(g => new ResumoFormaPagamentoDto
                {
                    FormaPagamento = g.Key,
                    Quantidade = g.Count(),
                    Total = g.Sum(v => v.ValorTotal)
                })
                .OrderBy(g => g.FormaPagamento)
                .ToList();

            return new ResumoCaixaDto
            {
                SessaoId = sessao.Id,
                MesaId = sessao.MesaId,
                MesaNumero = sessao.MesaId.HasValue
                    ? await _context.Mesas.AsNoTracking().Where(m => m.Id == sessao.MesaId.Value).Select(m => (int?)m.Numero).FirstOrDefaultAsync()
                    : null,
                DataAbertura = sessao.DataAbertura,
                ValorInicial = sessao.ValorInicial,
                QuantidadeVendas = quantidadeVendas,
                TotalVendas = totalVendas,
                ValorEsperado = sessao.ValorInicial + totalVendas,
                PorFormaPagamento = porForma
            };
        }

        /// <summary>
        /// Vendas concluidas vinculadas a sessao (exclui canceladas).
        ///
        /// Agrega em memoria: o provedor SQLite do EF Core nao traduz SUM sobre decimal
        /// ("SQLite cannot apply aggregate operator 'Sum' on expressions of type 'decimal'").
        /// A projecao e limitada aos valores da sessao, portanto o custo e controlado.
        /// </summary>
        private async Task<(int Quantidade, decimal Total)> CalcularTotaisAsync(int sessaoId)
        {
            var valores = await _context.Vendas
                .Where(v => v.SessaoCaixaId == sessaoId && v.Status == StatusVenda.Concluida)
                .Select(v => v.ValorTotal)
                .ToListAsync();

            if (valores.Count == 0) return (0, 0m);

            var total = 0m;
            foreach (var valor in valores) total += valor;

            return (valores.Count, total);
        }

        public async Task<SessaoCaixaDto> AbrirAsync(AbrirCaixaRequest request)
        {
            var empresaId = GetCurrentTenantId();
            var usuarioId = GetCurrentUserId();

            if (request.ValorInicial < 0)
                throw new ArgumentException("O valor inicial nao pode ser negativo.");

            var sessaoAberta = await _context.SessoesCaixa
                .AnyAsync(s => s.EmpresaId == empresaId && s.Status == StatusSessaoCaixa.Aberta);

            if (sessaoAberta)
                throw new InvalidOperationException("Ja existe uma sessao de caixa aberta para esta empresa.");

            Mesa? mesa = null;
            if (request.MesaId.HasValue)
            {
                mesa = await _context.Mesas.FirstOrDefaultAsync(m => m.Id == request.MesaId.Value && m.EmpresaId == empresaId);
                if (mesa == null)
                    throw new ArgumentException("Mesa invalida.");

                if (mesa.Status == StatusMesa.Ocupada)
                    throw new InvalidOperationException($"A Mesa {mesa.Numero} ja esta ocupada.");

                mesa.Status = StatusMesa.Ocupada;
                mesa.AtualizadoEm = DateTime.UtcNow;
            }

            var sessao = new SessaoCaixa
            {
                EmpresaId = empresaId,
                UsuarioId = usuarioId,
                MesaId = request.MesaId,
                DataAbertura = DateTime.UtcNow,
                ValorInicial = request.ValorInicial,
                Status = StatusSessaoCaixa.Aberta,
                CriadoEm = DateTime.UtcNow
            };

            _context.SessoesCaixa.Add(sessao);
            await _context.SaveChangesAsync();

            var usuario = await _context.Usuarios.FindAsync(usuarioId);

            return new SessaoCaixaDto
            {
                Id = sessao.Id,
                EmpresaId = sessao.EmpresaId,
                UsuarioId = sessao.UsuarioId,
                UsuarioNome = usuario?.Nome ?? string.Empty,
                MesaId = sessao.MesaId,
                MesaNumero = mesa?.Numero,
                DataAbertura = sessao.DataAbertura,
                ValorInicial = sessao.ValorInicial,
                Status = sessao.Status.ToString(),
                TotalVendas = 0m,
                QuantidadeVendas = 0
            };
        }

        public async Task<SessaoCaixaDto> FecharAsync(int sessaoId, FecharCaixaRequest request)
        {
            var empresaId = GetCurrentTenantId();

            var sessao = await _context.SessoesCaixa
                .Include(s => s.Usuario)
                .Include(s => s.Mesa)
                .FirstOrDefaultAsync(s => s.Id == sessaoId);

            if (sessao == null)
                throw new KeyNotFoundException($"Sessao de caixa com ID {sessaoId} nao encontrada.");

            if (sessao.EmpresaId != empresaId)
                throw new UnauthorizedAccessException("Acesso nao autorizado a esta sessao de caixa.");

            if (sessao.Status != StatusSessaoCaixa.Aberta)
                throw new InvalidOperationException("Nao e possivel fechar uma sessao ja encerrada.");

            if (request.ValorFechamento < 0)
                throw new ArgumentException("O valor de fechamento nao pode ser negativo.");

            if (sessao.MesaId.HasValue)
            {
                var mesa = await _context.Mesas.FirstOrDefaultAsync(m => m.Id == sessao.MesaId.Value);
                if (mesa != null)
                {
                    mesa.Status = StatusMesa.Livre;
                    mesa.AtualizadoEm = DateTime.UtcNow;
                }
            }

            sessao.DataFechamento = DateTime.UtcNow;
            sessao.ValorFechamento = request.ValorFechamento;
            sessao.Status = StatusSessaoCaixa.Fechada;
            sessao.AtualizadoEm = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            var (quantidadeVendas, totalVendas) = await CalcularTotaisAsync(sessao.Id);

            return new SessaoCaixaDto
            {
                Id = sessao.Id,
                EmpresaId = sessao.EmpresaId,
                UsuarioId = sessao.UsuarioId,
                UsuarioNome = sessao.Usuario.Nome,
                MesaId = sessao.MesaId,
                MesaNumero = sessao.Mesa?.Numero,
                DataAbertura = sessao.DataAbertura,
                ValorInicial = sessao.ValorInicial,
                DataFechamento = sessao.DataFechamento,
                ValorFechamento = sessao.ValorFechamento,
                Status = sessao.Status.ToString(),
                TotalVendas = totalVendas,
                QuantidadeVendas = quantidadeVendas
            };
        }
    }
}

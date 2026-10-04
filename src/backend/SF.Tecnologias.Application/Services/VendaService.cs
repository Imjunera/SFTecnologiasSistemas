using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SF.Tecnologias.Application.DTOs;
using SF.Tecnologias.Domain;
using SF.Tecnologias.Infrastructure.Persistence;

namespace SF.Tecnologias.Application.Services
{
    public class VendaService : IVendaService
    {
        private static readonly string[] FormasPagamentoValidas = { "dinheiro", "cartao", "pix" };

        private readonly AppDbContext _context;
        private readonly ITenantProvider _tenantProvider;

        public VendaService(AppDbContext context, ITenantProvider tenantProvider)
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

        private int? GetCurrentUserId()
        {
            return _tenantProvider.GetUsuarioId();
        }

        private static string NormalizarFormaPagamento(string? forma)
        {
            var semAcentos = new StringBuilder();
            var normalizado = (forma ?? string.Empty).Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
            foreach (var c in normalizado)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                    semAcentos.Append(c);
            }

            var valor = semAcentos.ToString();
            if (valor == "dinheiro" || valor == "pix")
                return valor;

            // Aceita os rotulos usados pelo operador para o mesmo meio.
            if (valor == "cartao" || valor == "cartoes" || valor == "credito" || valor == "debito")
                return "cartao";

            throw new ArgumentException("Forma de pagamento invalida. Use dinheiro, cartao ou pix.");
        }

        public async Task<IEnumerable<VendaDto>> ObterTodasAsync(DateTime? dataInicio = null, DateTime? dataFim = null, int? sessaoCaixaId = null)
        {
            var empresaId = GetCurrentTenantId();
            IQueryable<Venda> query = _context.Vendas.AsNoTracking()
                .Include(v => v.Cliente)
                .Include(v => v.Usuario)
                .Include(v => v.Itens);

            query = query.Where(v => v.EmpresaId == empresaId);

            if (sessaoCaixaId.HasValue)
                query = query.Where(v => v.SessaoCaixaId == sessaoCaixaId.Value);

            if (dataInicio.HasValue)
                query = query.Where(v => v.DataVenda >= dataInicio.Value);

            if (dataFim.HasValue)
                query = query.Where(v => v.DataVenda <= dataFim.Value);

            var vendas = await query.OrderByDescending(v => v.DataVenda).ToListAsync();
            return vendas.Select(Mapear).ToList();
        }

        public async Task<VendaDto?> ObterPorIdAsync(int id)
        {
            var empresaId = GetCurrentTenantId();
            var venda = await _context.Vendas.AsNoTracking()
                .Include(v => v.Cliente)
                .Include(v => v.Usuario)
                .Include(v => v.Itens)
                .FirstOrDefaultAsync(v => v.Id == id);

            if (venda == null || venda.EmpresaId != empresaId)
                return null;

            return Mapear(venda);
        }

        public async Task<VendaDto> CriarAsync(CriarVendaRequest request)
        {
            var empresaId = GetCurrentTenantId();

            if (request.Itens == null || request.Itens.Count == 0)
                throw new ArgumentException("A venda precisa ter pelo menos um item.");

            if (request.Itens.Any(i => i.Quantidade <= 0))
                throw new ArgumentException("A quantidade de cada item deve ser maior que zero.");

            var formaPagamento = NormalizarFormaPagamento(request.FormaPagamento);

            // Merge de itens repetidos (mesmo produto duas vezes no carrinho)
            var itensAgrupados = request.Itens
                .GroupBy(i => i.ProdutoId)
                .Select(g => new CriarVendaItemRequest { ProdutoId = g.Key, Quantidade = g.Sum(i => i.Quantidade) })
                .ToList();

            var produtoIds = itensAgrupados.Select(i => i.ProdutoId).Distinct().ToList();
            var produtos = await _context.Produtos
                .AsNoTracking()
                .Where(p => produtoIds.Contains(p.Id) && p.EmpresaId == empresaId)
                .ToListAsync();

            if (produtos.Count != produtoIds.Count)
                throw new ArgumentException("Produto inexistente ou de outra empresa.");

            var produtoInativo = produtos.FirstOrDefault(p => !p.Ativo);
            if (produtoInativo != null)
                throw new ArgumentException($"O produto '{produtoInativo.Nome}' esta inativo e nao pode ser vendido.");

            // Sessao de caixa: usa a informada (deve estar aberta) ou a sessao aberta atual.
            int? sessaoCaixaId = request.SessaoCaixaId;
            if (sessaoCaixaId.HasValue)
            {
                var sessao = await _context.SessoesCaixa
                    .AsNoTracking()
                    .FirstOrDefaultAsync(s => s.Id == sessaoCaixaId.Value && s.EmpresaId == empresaId);

                if (sessao == null)
                    throw new ArgumentException("Sessao de caixa inexistente.");

                if (sessao.Status != StatusSessaoCaixa.Aberta)
                    throw new InvalidOperationException("Nao e possivel registrar venda em sessao de caixa ja encerrada.");
            }
            else
            {
                sessaoCaixaId = await _context.SessoesCaixa
                    .Where(s => s.EmpresaId == empresaId && s.Status == StatusSessaoCaixa.Aberta)
                    .Select(s => (int?)s.Id)
                    .FirstOrDefaultAsync();
            }

            int? clienteId = request.ClienteId;
            if (clienteId.HasValue)
            {
                var clienteOk = await _context.Clientes
                    .AnyAsync(c => c.Id == clienteId.Value && c.EmpresaId == empresaId && c.Ativo);

                if (!clienteOk)
                    throw new ArgumentException("Cliente invalido ou inativo.");
            }

            // O preco vem SEMPRE do cadastro: o carrinho do cliente nao define preco.
            var produtosPorId = produtos.ToDictionary(p => p.Id);
            var venda = new Venda
            {
                EmpresaId = empresaId,
                UsuarioId = GetCurrentUserId(),
                SessaoCaixaId = sessaoCaixaId,
                ClienteId = clienteId,
                DataVenda = DateTime.UtcNow,
                FormaPagamento = formaPagamento,
                Status = StatusVenda.Concluida,
                CriadoEm = DateTime.UtcNow
            };

            foreach (var item in itensAgrupados)
            {
                var produto = produtosPorId[item.ProdutoId];
                var subtotal = produto.PrecoVenda * item.Quantidade;

                venda.Itens.Add(new VendaItem
                {
                    EmpresaId = empresaId,
                    ProdutoId = produto.Id,
                    ProdutoNome = produto.Nome,
                    Quantidade = item.Quantidade,
                    PrecoUnitario = produto.PrecoVenda,
                    Subtotal = subtotal,
                    CriadoEm = DateTime.UtcNow
                });
            }

            venda.ValorTotal = venda.Itens.Sum(i => i.Subtotal);

            _context.Vendas.Add(venda);
            await _context.SaveChangesAsync();

            return Mapear(venda);
        }

        public async Task<VendaDto> AtualizarAsync(int id, AtualizarVendaRequest request)
        {
            var empresaId = GetCurrentTenantId();

            if (!Enum.TryParse<StatusVenda>(request.Status, true, out var status))
                throw new ArgumentException("Status invalido. Use Concluida ou Cancelada.");

            var venda = await _context.Vendas
                .Include(v => v.Itens)
                .Include(v => v.Cliente)
                .Include(v => v.Usuario)
                .FirstOrDefaultAsync(v => v.Id == id);

            if (venda == null)
                throw new KeyNotFoundException($"Venda com ID {id} nao encontrada.");

            if (venda.EmpresaId != empresaId)
                throw new UnauthorizedAccessException("Acesso nao autorizado a esta venda.");

            venda.Status = status;
            venda.AtualizadoEm = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return Mapear(venda);
        }

        public async Task<bool> ExcluirAsync(int id)
        {
            var empresaId = GetCurrentTenantId();
            var venda = await _context.Vendas.FirstOrDefaultAsync(v => v.Id == id);

            if (venda == null)
                return false;

            if (venda.EmpresaId != empresaId)
                throw new UnauthorizedAccessException("Acesso nao autorizado a esta venda.");

            if (venda.Status == StatusVenda.Concluida && venda.SessaoCaixaId.HasValue)
            {
                var sessaoEncerrada = await _context.SessoesCaixa
                    .AsNoTracking()
                    .AnyAsync(s => s.Id == venda.SessaoCaixaId.Value &&
                                   s.Status == StatusSessaoCaixa.Fechada);

                if (sessaoEncerrada)
                    throw new InvalidOperationException(
                        "Nao e possivel excluir uma venda de sessao ja encerrada. Cancele a venda para preservar o fechamento.");
            }

            _context.Vendas.Remove(venda);
            await _context.SaveChangesAsync();
            return true;
        }

        private static VendaDto Mapear(Venda venda)
        {
            return new VendaDto
            {
                Id = venda.Id,
                EmpresaId = venda.EmpresaId,
                UsuarioId = venda.UsuarioId,
                UsuarioNome = venda.Usuario?.Nome,
                SessaoCaixaId = venda.SessaoCaixaId,
                ClienteId = venda.ClienteId,
                ClienteNome = venda.Cliente?.Nome,
                DataVenda = venda.DataVenda,
                FormaPagamento = venda.FormaPagamento,
                ValorTotal = venda.ValorTotal,
                Status = venda.Status.ToString(),
                QuantidadeItens = venda.Itens?.Sum(i => i.Quantidade) ?? 0,
                Itens = venda.Itens?.Select(i => new VendaItemDto
                {
                    Id = i.Id,
                    ProdutoId = i.ProdutoId,
                    ProdutoNome = i.ProdutoNome,
                    Quantidade = i.Quantidade,
                    PrecoUnitario = i.PrecoUnitario,
                    Subtotal = i.Subtotal
                }).ToList()
            };
        }
    }
}

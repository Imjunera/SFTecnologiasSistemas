using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SF.Tecnologias.Application.DTOs;
using SF.Tecnologias.Domain;
using SF.Tecnologias.Infrastructure.Persistence;

namespace SF.Tecnologias.Application.Services
{
    public class ProdutoService : IProdutoService
    {
        private readonly AppDbContext _context;
        private readonly ITenantProvider _tenantProvider;

        public ProdutoService(AppDbContext context, ITenantProvider tenantProvider)
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

        public async Task<IEnumerable<ProdutoDto>> ObterTodosAsync(string? busca = null, bool apenasAtivos = false)
        {
            var empresaId = GetCurrentTenantId();
            IQueryable<Produto> query = _context.Produtos.AsNoTracking().Include(p => p.Categoria);

            if (apenasAtivos)
                query = query.Where(p => p.Ativo);

            if (!string.IsNullOrWhiteSpace(busca))
            {
                var termo = busca.Trim().ToLower();
                query = query.Where(p => p.Nome.ToLower().Contains(termo) || p.Codigo.ToLower().Contains(termo));
            }

            return await query
                .OrderBy(p => p.Nome)
                .Select(p => new ProdutoDto
                {
                    Id = p.Id,
                    EmpresaId = p.EmpresaId,
                    CategoriaId = p.CategoriaId,
                    CategoriaNome = p.Categoria != null ? p.Categoria.Nome : null,
                    Nome = p.Nome,
                    Descricao = p.Descricao,
                    Codigo = p.Codigo,
                    PrecoVenda = p.PrecoVenda,
                    PrecoCusto = p.PrecoCusto,
                    Ativo = p.Ativo,
                    CriadoEm = p.CriadoEm,
                    AtualizadoEm = p.AtualizadoEm
                })
                .ToListAsync();
        }

        public async Task<ProdutoDto?> ObterPorIdAsync(int id)
        {
            var empresaId = GetCurrentTenantId();
            var p = await _context.Produtos.AsNoTracking().Include(x => x.Categoria).FirstOrDefaultAsync(x => x.Id == id);
            if (p == null || p.EmpresaId != empresaId) return null;

            return new ProdutoDto
            {
                Id = p.Id,
                EmpresaId = p.EmpresaId,
                CategoriaId = p.CategoriaId,
                CategoriaNome = p.Categoria?.Nome,
                Nome = p.Nome,
                Descricao = p.Descricao,
                Codigo = p.Codigo,
                PrecoVenda = p.PrecoVenda,
                PrecoCusto = p.PrecoCusto,
                Ativo = p.Ativo,
                CriadoEm = p.CriadoEm,
                AtualizadoEm = p.AtualizadoEm
            };
        }

        public async Task<ProdutoDto> CriarAsync(CriarProdutoRequest request)
        {
            var empresaId = GetCurrentTenantId();

            if (string.IsNullOrWhiteSpace(request.Nome))
                throw new ArgumentException("O nome do produto e obrigatorio.");

            if (string.IsNullOrWhiteSpace(request.Codigo))
                throw new ArgumentException("O codigo do produto e obrigatorio.");

            if (request.PrecoVenda <= 0)
                throw new ArgumentException("O preco de venda deve ser maior que zero.");

            var codigoExiste = await _context.Produtos
                .AnyAsync(p => p.Codigo == request.Codigo.Trim() && p.EmpresaId == empresaId);

            if (codigoExiste)
                throw new InvalidOperationException($"Ja existe um produto cadastrado com o codigo '{request.Codigo}'.");

            if (request.CategoriaId.HasValue)
            {
                var categoriaExiste = await _context.Categorias
                    .AnyAsync(c => c.Id == request.CategoriaId.Value && c.EmpresaId == empresaId && c.Ativo);
                if (!categoriaExiste)
                    throw new ArgumentException("Categoria invalida ou inativa.");
            }

            var produto = new Produto
            {
                EmpresaId = empresaId,
                CategoriaId = request.CategoriaId,
                Nome = request.Nome.Trim(),
                Descricao = request.Descricao?.Trim(),
                Codigo = request.Codigo.Trim(),
                PrecoVenda = request.PrecoVenda,
                PrecoCusto = request.PrecoCusto,
                Ativo = true,
                CriadoEm = DateTime.UtcNow
            };

            _context.Produtos.Add(produto);
            await _context.SaveChangesAsync();

            return new ProdutoDto
            {
                Id = produto.Id,
                EmpresaId = produto.EmpresaId,
                CategoriaId = produto.CategoriaId,
                Nome = produto.Nome,
                Descricao = produto.Descricao,
                Codigo = produto.Codigo,
                PrecoVenda = produto.PrecoVenda,
                PrecoCusto = produto.PrecoCusto,
                Ativo = produto.Ativo,
                CriadoEm = produto.CriadoEm
            };
        }

        public async Task<ProdutoDto> AtualizarAsync(int id, AtualizarProdutoRequest request)
        {
            var empresaId = GetCurrentTenantId();

            if (string.IsNullOrWhiteSpace(request.Nome))
                throw new ArgumentException("O nome do produto e obrigatorio.");

            if (string.IsNullOrWhiteSpace(request.Codigo))
                throw new ArgumentException("O codigo do produto e obrigatorio.");

            if (request.PrecoVenda <= 0)
                throw new ArgumentException("O preco de venda deve ser maior que zero.");

            var produto = await _context.Produtos.FirstOrDefaultAsync(p => p.Id == id);
            if (produto == null)
                throw new KeyNotFoundException($"Produto com ID {id} nao encontrado.");

            if (produto.EmpresaId != empresaId)
                throw new UnauthorizedAccessException("Acesso nao autorizado a este produto.");

            var codigoExiste = await _context.Produtos
                .AnyAsync(p => p.Codigo == request.Codigo.Trim() && p.EmpresaId == empresaId && p.Id != id);

            if (codigoExiste)
                throw new InvalidOperationException($"Ja existe outro produto com o codigo '{request.Codigo}'.");

            if (request.CategoriaId.HasValue)
            {
                var categoriaExiste = await _context.Categorias
                    .AnyAsync(c => c.Id == request.CategoriaId.Value && c.EmpresaId == empresaId && c.Ativo);
                if (!categoriaExiste)
                    throw new ArgumentException("Categoria invalida ou inativa.");
            }

            produto.CategoriaId = request.CategoriaId;
            produto.Nome = request.Nome.Trim();
            produto.Descricao = request.Descricao?.Trim();
            produto.Codigo = request.Codigo.Trim();
            produto.PrecoVenda = request.PrecoVenda;
            produto.PrecoCusto = request.PrecoCusto;
            produto.Ativo = request.Ativo;
            produto.AtualizadoEm = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            return new ProdutoDto
            {
                Id = produto.Id,
                EmpresaId = produto.EmpresaId,
                CategoriaId = produto.CategoriaId,
                Nome = produto.Nome,
                Descricao = produto.Descricao,
                Codigo = produto.Codigo,
                PrecoVenda = produto.PrecoVenda,
                PrecoCusto = produto.PrecoCusto,
                Ativo = produto.Ativo,
                CriadoEm = produto.CriadoEm,
                AtualizadoEm = produto.AtualizadoEm
            };
        }

        public async Task<bool> InativarAsync(int id)
        {
            var empresaId = GetCurrentTenantId();
            var produto = await _context.Produtos.FirstOrDefaultAsync(p => p.Id == id);
            if (produto == null) return false;

            if (produto.EmpresaId != empresaId)
                throw new UnauthorizedAccessException("Acesso nao autorizado a este produto.");

            produto.Ativo = false;
            produto.AtualizadoEm = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return true;
        }
        public async Task<bool> ExcluirAsync(int id)
        {
            var empresaId = GetCurrentTenantId();
            var registro = await _context.Produtos.FirstOrDefaultAsync(x => x.Id == id);
            if (registro == null) return false;
            if (registro.EmpresaId != empresaId) throw new UnauthorizedAccessException("Acesso nao autorizado.");

            _context.Produtos.Remove(registro);
            await _context.SaveChangesAsync();
            return true;
        }
    }
}


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
    public class CategoriaService : ICategoriaService
    {
        private readonly AppDbContext _context;
        private readonly ITenantProvider _tenantProvider;

        public CategoriaService(AppDbContext context, ITenantProvider tenantProvider)
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

        public async Task<IEnumerable<CategoriaDto>> ObterTodosAsync(string? busca = null, bool apenasAtivos = false)
        {
            var empresaId = GetCurrentTenantId();
            var query = _context.Categorias.AsNoTracking();

            if (apenasAtivos)
                query = query.Where(c => c.Ativo);

            if (!string.IsNullOrWhiteSpace(busca))
            {
                var termo = busca.Trim().ToLower();
                query = query.Where(c => c.Nome.ToLower().Contains(termo));
            }

            return await query
                .OrderBy(c => c.Ordem).ThenBy(c => c.Nome)
                .Select(c => new CategoriaDto
                {
                    Id = c.Id,
                    EmpresaId = c.EmpresaId,
                    Nome = c.Nome,
                    Descricao = c.Descricao,
                    Ordem = c.Ordem,
                    Ativo = c.Ativo,
                    CriadoEm = c.CriadoEm,
                    AtualizadoEm = c.AtualizadoEm
                })
                .ToListAsync();
        }

        public async Task<CategoriaDto?> ObterPorIdAsync(int id)
        {
            var empresaId = GetCurrentTenantId();
            var c = await _context.Categorias.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (c == null || c.EmpresaId != empresaId) return null;

            return new CategoriaDto
            {
                Id = c.Id,
                EmpresaId = c.EmpresaId,
                Nome = c.Nome,
                Descricao = c.Descricao,
                Ordem = c.Ordem,
                Ativo = c.Ativo,
                CriadoEm = c.CriadoEm,
                AtualizadoEm = c.AtualizadoEm
            };
        }

        public async Task<CategoriaDto> CriarAsync(CriarCategoriaRequest request)
        {
            var empresaId = GetCurrentTenantId();

            if (string.IsNullOrWhiteSpace(request.Nome))
                throw new ArgumentException("O nome da categoria e obrigatorio.");

            var nomeExiste = await _context.Categorias
                .AnyAsync(c => c.Nome == request.Nome.Trim() && c.EmpresaId == empresaId);

            if (nomeExiste)
                throw new InvalidOperationException($"Ja existe uma categoria com o nome '{request.Nome}' nesta empresa.");

            var categoria = new Categoria
            {
                EmpresaId = empresaId,
                Nome = request.Nome.Trim(),
                Descricao = request.Descricao?.Trim(),
                Ordem = request.Ordem ?? 0,
                Ativo = true,
                CriadoEm = DateTime.UtcNow
            };

            _context.Categorias.Add(categoria);
            await _context.SaveChangesAsync();

            return new CategoriaDto
            {
                Id = categoria.Id,
                EmpresaId = categoria.EmpresaId,
                Nome = categoria.Nome,
                Descricao = categoria.Descricao,
                Ordem = categoria.Ordem,
                Ativo = categoria.Ativo,
                CriadoEm = categoria.CriadoEm
            };
        }

        public async Task<CategoriaDto> AtualizarAsync(int id, AtualizarCategoriaRequest request)
        {
            var empresaId = GetCurrentTenantId();

            if (string.IsNullOrWhiteSpace(request.Nome))
                throw new ArgumentException("O nome da categoria e obrigatorio.");

            var categoria = await _context.Categorias.FirstOrDefaultAsync(c => c.Id == id);
            if (categoria == null)
                throw new KeyNotFoundException($"Categoria com ID {id} nao encontrada.");

            if (categoria.EmpresaId != empresaId)
                throw new UnauthorizedAccessException("Acesso nao autorizado a esta categoria.");

            var nomeExiste = await _context.Categorias
                .AnyAsync(c => c.Nome == request.Nome.Trim() && c.EmpresaId == empresaId && c.Id != id);

            if (nomeExiste)
                throw new InvalidOperationException($"Ja existe outra categoria com o nome '{request.Nome}'.");

            categoria.Nome = request.Nome.Trim();
            categoria.Descricao = request.Descricao?.Trim();
            categoria.Ordem = request.Ordem ?? categoria.Ordem;
            categoria.Ativo = request.Ativo;
            categoria.AtualizadoEm = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            return new CategoriaDto
            {
                Id = categoria.Id,
                EmpresaId = categoria.EmpresaId,
                Nome = categoria.Nome,
                Descricao = categoria.Descricao,
                Ordem = categoria.Ordem,
                Ativo = categoria.Ativo,
                CriadoEm = categoria.CriadoEm,
                AtualizadoEm = categoria.AtualizadoEm
            };
        }

        public async Task<bool> InativarAsync(int id)
        {
            var empresaId = GetCurrentTenantId();
            var categoria = await _context.Categorias.FirstOrDefaultAsync(c => c.Id == id);
            if (categoria == null) return false;

            if (categoria.EmpresaId != empresaId)
                throw new UnauthorizedAccessException("Acesso nao autorizado a esta categoria.");

            var temProdutos = await _context.Produtos.AnyAsync(p => p.CategoriaId == id && p.Ativo);
            if (temProdutos)
                throw new InvalidOperationException("Nao e possivel inativar uma categoria com produtos ativos vinculados.");

            categoria.Ativo = false;
            categoria.AtualizadoEm = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return true;
        }
        public async Task<bool> ExcluirAsync(int id)
        {
            var empresaId = GetCurrentTenantId();
            var registro = await _context.Categorias.FirstOrDefaultAsync(x => x.Id == id);
            if (registro == null) return false;
            if (registro.EmpresaId != empresaId) throw new UnauthorizedAccessException("Acesso nao autorizado.");

            var temProdutos = await _context.Produtos.AnyAsync(p => p.CategoriaId == id);
            if (temProdutos)
                throw new InvalidOperationException("Nao e possivel excluir uma categoria com produtos vinculados.");

            _context.Categorias.Remove(registro);
            await _context.SaveChangesAsync();
            return true;
        }
    }
}


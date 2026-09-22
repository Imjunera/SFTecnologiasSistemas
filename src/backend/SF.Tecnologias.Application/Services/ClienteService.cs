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
    public class ClienteService : IClienteService
    {
        private readonly AppDbContext _context;
        private readonly ITenantProvider _tenantProvider;

        public ClienteService(AppDbContext context, ITenantProvider tenantProvider)
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

        public async Task<IEnumerable<ClienteDto>> ObterTodosAsync(string? busca = null, bool apenasAtivos = false)
        {
            var empresaId = GetCurrentTenantId();
            var query = _context.Clientes.AsNoTracking();

            if (apenasAtivos)
                query = query.Where(c => c.Ativo);

            if (!string.IsNullOrWhiteSpace(busca))
            {
                var termo = busca.Trim().ToLower();
                query = query.Where(c =>
                    c.Nome.ToLower().Contains(termo) ||
                    (c.Telefone != null && c.Telefone.Contains(termo)) ||
                    (c.Cpf != null && c.Cpf.Contains(termo)) ||
                    (c.Email != null && c.Email.ToLower().Contains(termo)));
            }

            return await query
                .OrderBy(c => c.Nome)
                .Select(c => new ClienteDto
                {
                    Id = c.Id,
                    EmpresaId = c.EmpresaId,
                    Nome = c.Nome,
                    Telefone = c.Telefone,
                    Cpf = c.Cpf,
                    Email = c.Email,
                    Endereco = c.Endereco,
                    Observacoes = c.Observacoes,
                    Ativo = c.Ativo,
                    CriadoEm = c.CriadoEm,
                    AtualizadoEm = c.AtualizadoEm
                })
                .ToListAsync();
        }

        public async Task<ClienteDto?> ObterPorIdAsync(int id)
        {
            var empresaId = GetCurrentTenantId();
            var c = await _context.Clientes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (c == null || c.EmpresaId != empresaId) return null;

            return new ClienteDto
            {
                Id = c.Id,
                EmpresaId = c.EmpresaId,
                Nome = c.Nome,
                Telefone = c.Telefone,
                Cpf = c.Cpf,
                Email = c.Email,
                Endereco = c.Endereco,
                Observacoes = c.Observacoes,
                Ativo = c.Ativo,
                CriadoEm = c.CriadoEm,
                AtualizadoEm = c.AtualizadoEm
            };
        }

        public async Task<ClienteDto> CriarAsync(CriarClienteRequest request)
        {
            var empresaId = GetCurrentTenantId();

            if (string.IsNullOrWhiteSpace(request.Nome))
                throw new ArgumentException("O nome do cliente e obrigatorio.");

            var cliente = new Cliente
            {
                EmpresaId = empresaId,
                Nome = request.Nome.Trim(),
                Telefone = request.Telefone?.Trim(),
                Cpf = request.Cpf?.Trim(),
                Email = request.Email?.Trim(),
                Endereco = request.Endereco?.Trim(),
                Observacoes = request.Observacoes?.Trim(),
                Ativo = true,
                CriadoEm = DateTime.UtcNow
            };

            _context.Clientes.Add(cliente);
            await _context.SaveChangesAsync();

            return new ClienteDto
            {
                Id = cliente.Id,
                EmpresaId = cliente.EmpresaId,
                Nome = cliente.Nome,
                Telefone = cliente.Telefone,
                Cpf = cliente.Cpf,
                Email = cliente.Email,
                Endereco = cliente.Endereco,
                Observacoes = cliente.Observacoes,
                Ativo = cliente.Ativo,
                CriadoEm = cliente.CriadoEm
            };
        }

        public async Task<ClienteDto> AtualizarAsync(int id, AtualizarClienteRequest request)
        {
            var empresaId = GetCurrentTenantId();

            if (string.IsNullOrWhiteSpace(request.Nome))
                throw new ArgumentException("O nome do cliente e obrigatorio.");

            var cliente = await _context.Clientes.FirstOrDefaultAsync(c => c.Id == id);
            if (cliente == null)
                throw new KeyNotFoundException($"Cliente com ID {id} nao encontrado.");

            if (cliente.EmpresaId != empresaId)
                throw new UnauthorizedAccessException("Acesso nao autorizado a este cliente.");

            cliente.Nome = request.Nome.Trim();
            cliente.Telefone = request.Telefone?.Trim();
            cliente.Cpf = request.Cpf?.Trim();
            cliente.Email = request.Email?.Trim();
            cliente.Endereco = request.Endereco?.Trim();
            cliente.Observacoes = request.Observacoes?.Trim();
            cliente.Ativo = request.Ativo;
            cliente.AtualizadoEm = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            return new ClienteDto
            {
                Id = cliente.Id,
                EmpresaId = cliente.EmpresaId,
                Nome = cliente.Nome,
                Telefone = cliente.Telefone,
                Cpf = cliente.Cpf,
                Email = cliente.Email,
                Endereco = cliente.Endereco,
                Observacoes = cliente.Observacoes,
                Ativo = cliente.Ativo,
                CriadoEm = cliente.CriadoEm,
                AtualizadoEm = cliente.AtualizadoEm
            };
        }

        public async Task<bool> InativarAsync(int id)
        {
            var empresaId = GetCurrentTenantId();
            var cliente = await _context.Clientes.FirstOrDefaultAsync(c => c.Id == id);
            if (cliente == null) return false;

            if (cliente.EmpresaId != empresaId)
                throw new UnauthorizedAccessException("Acesso nao autorizado a este cliente.");

            cliente.Ativo = false;
            cliente.AtualizadoEm = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return true;
        }
        public async Task<bool> ExcluirAsync(int id)
        {
            var empresaId = GetCurrentTenantId();
            var registro = await _context.Clientes.FirstOrDefaultAsync(x => x.Id == id);
            if (registro == null) return false;
            if (registro.EmpresaId != empresaId) throw new UnauthorizedAccessException("Acesso nao autorizado.");
            _context.Clientes.Remove(registro);
            await _context.SaveChangesAsync();
            return true;
        }
    }
}


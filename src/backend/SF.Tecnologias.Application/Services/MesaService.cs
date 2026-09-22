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
    public class MesaService : IMesaService
    {
        private readonly AppDbContext _context;
        private readonly ITenantProvider _tenantProvider;

        public MesaService(AppDbContext context, ITenantProvider tenantProvider)
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

        public async Task<IEnumerable<MesaDto>> ObterTodosAsync(string? busca = null, bool apenasAtivos = false)
        {
            var empresaId = GetCurrentTenantId();
            var query = _context.Mesas.AsNoTracking();

            if (apenasAtivos)
                query = query.Where(m => m.Ativo);

            if (!string.IsNullOrWhiteSpace(busca))
            {
                var termo = busca.Trim().ToLower();
                query = query.Where(m =>
                    m.Numero.ToString().Contains(termo) ||
                    (m.Descricao != null && m.Descricao.ToLower().Contains(termo)));
            }

            return await query
                .OrderBy(m => m.Numero)
                .Select(m => new MesaDto
                {
                    Id = m.Id,
                    EmpresaId = m.EmpresaId,
                    Numero = m.Numero,
                    Descricao = m.Descricao,
                    Capacidade = m.Capacidade,
                    Status = m.Status.ToString(),
                    Ativo = m.Ativo,
                    CriadoEm = m.CriadoEm,
                    AtualizadoEm = m.AtualizadoEm
                })
                .ToListAsync();
        }

        public async Task<MesaDto?> ObterPorIdAsync(int id)
        {
            var empresaId = GetCurrentTenantId();
            var m = await _context.Mesas.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (m == null || m.EmpresaId != empresaId) return null;

            return new MesaDto
            {
                Id = m.Id,
                EmpresaId = m.EmpresaId,
                Numero = m.Numero,
                Descricao = m.Descricao,
                Capacidade = m.Capacidade,
                Status = m.Status.ToString(),
                Ativo = m.Ativo,
                CriadoEm = m.CriadoEm,
                AtualizadoEm = m.AtualizadoEm
            };
        }

        public async Task<MesaDto> CriarAsync(CriarMesaRequest request)
        {
            var empresaId = GetCurrentTenantId();

            if (request.Numero <= 0)
                throw new ArgumentException("O numero da mesa deve ser maior que zero.");

            var numeroExiste = await _context.Mesas
                .AnyAsync(m => m.Numero == request.Numero && m.EmpresaId == empresaId);

            if (numeroExiste)
                throw new InvalidOperationException($"Ja existe uma mesa com o numero {request.Numero} nesta empresa.");

            var mesa = new Mesa
            {
                EmpresaId = empresaId,
                Numero = request.Numero,
                Descricao = request.Descricao?.Trim(),
                Capacidade = request.Capacidade ?? 4,
                Status = StatusMesa.Livre,
                Ativo = true,
                CriadoEm = DateTime.UtcNow
            };

            _context.Mesas.Add(mesa);
            await _context.SaveChangesAsync();

            return new MesaDto
            {
                Id = mesa.Id,
                EmpresaId = mesa.EmpresaId,
                Numero = mesa.Numero,
                Descricao = mesa.Descricao,
                Capacidade = mesa.Capacidade,
                Status = mesa.Status.ToString(),
                Ativo = mesa.Ativo,
                CriadoEm = mesa.CriadoEm
            };
        }

        public async Task<MesaDto> AtualizarAsync(int id, AtualizarMesaRequest request)
        {
            var empresaId = GetCurrentTenantId();

            if (request.Numero <= 0)
                throw new ArgumentException("O numero da mesa deve ser maior que zero.");

            var mesa = await _context.Mesas.FirstOrDefaultAsync(m => m.Id == id);
            if (mesa == null)
                throw new KeyNotFoundException($"Mesa com ID {id} nao encontrada.");

            if (mesa.EmpresaId != empresaId)
                throw new UnauthorizedAccessException("Acesso nao autorizado a esta mesa.");

            var numeroExiste = await _context.Mesas
                .AnyAsync(m => m.Numero == request.Numero && m.EmpresaId == empresaId && m.Id != id);

            if (numeroExiste)
                throw new InvalidOperationException($"Ja existe outra mesa com o numero {request.Numero}.");

            if (!Enum.TryParse<StatusMesa>(request.Status, true, out var status))
                throw new ArgumentException("Status invalido.");

            mesa.Numero = request.Numero;
            mesa.Descricao = request.Descricao?.Trim();
            mesa.Capacidade = request.Capacidade ?? mesa.Capacidade;
            mesa.Status = status;
            mesa.Ativo = request.Ativo;
            mesa.AtualizadoEm = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            return new MesaDto
            {
                Id = mesa.Id,
                EmpresaId = mesa.EmpresaId,
                Numero = mesa.Numero,
                Descricao = mesa.Descricao,
                Capacidade = mesa.Capacidade,
                Status = mesa.Status.ToString(),
                Ativo = mesa.Ativo,
                CriadoEm = mesa.CriadoEm,
                AtualizadoEm = mesa.AtualizadoEm
            };
        }

        public async Task<bool> InativarAsync(int id)
        {
            var empresaId = GetCurrentTenantId();
            var mesa = await _context.Mesas.FirstOrDefaultAsync(m => m.Id == id);
            if (mesa == null) return false;

            if (mesa.EmpresaId != empresaId)
                throw new UnauthorizedAccessException("Acesso nao autorizado a esta mesa.");

            mesa.Ativo = false;
            mesa.AtualizadoEm = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return true;
        }
        public async Task<bool> ExcluirAsync(int id)
        {
            var empresaId = GetCurrentTenantId();
            var registro = await _context.Mesas.FirstOrDefaultAsync(x => x.Id == id);
            if (registro == null) return false;
            if (registro.EmpresaId != empresaId) throw new UnauthorizedAccessException("Acesso nao autorizado.");
            _context.Mesas.Remove(registro);
            await _context.SaveChangesAsync();
            return true;
        }
    }
}


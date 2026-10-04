using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SF.Tecnologias.Application.DTOs;

namespace SF.Tecnologias.Application.Services
{
    public interface IVendaService
    {
        Task<IEnumerable<VendaDto>> ObterTodasAsync(DateTime? dataInicio = null, DateTime? dataFim = null, int? sessaoCaixaId = null);
        Task<VendaDto?> ObterPorIdAsync(int id);
        Task<VendaDto> CriarAsync(CriarVendaRequest request);
        Task<VendaDto> AtualizarAsync(int id, AtualizarVendaRequest request);
        Task<bool> ExcluirAsync(int id);
    }
}

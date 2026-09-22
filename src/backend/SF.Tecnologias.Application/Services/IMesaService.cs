using System.Collections.Generic;
using System.Threading.Tasks;
using SF.Tecnologias.Application.DTOs;

namespace SF.Tecnologias.Application.Services
{
    public interface IMesaService
    {
        Task<IEnumerable<MesaDto>> ObterTodosAsync(string? busca = null, bool apenasAtivos = false);
        Task<MesaDto?> ObterPorIdAsync(int id);
        Task<MesaDto> CriarAsync(CriarMesaRequest request);
        Task<MesaDto> AtualizarAsync(int id, AtualizarMesaRequest request);
        Task<bool> InativarAsync(int id);
        Task<bool> ExcluirAsync(int id);
    }
}


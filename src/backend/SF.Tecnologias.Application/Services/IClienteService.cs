using System.Collections.Generic;
using System.Threading.Tasks;
using SF.Tecnologias.Application.DTOs;

namespace SF.Tecnologias.Application.Services
{
    public interface IClienteService
    {
        Task<IEnumerable<ClienteDto>> ObterTodosAsync(string? busca = null, bool apenasAtivos = false);
        Task<ClienteDto?> ObterPorIdAsync(int id);
        Task<ClienteDto> CriarAsync(CriarClienteRequest request);
        Task<ClienteDto> AtualizarAsync(int id, AtualizarClienteRequest request);
        Task<bool> InativarAsync(int id);
        Task<bool> ExcluirAsync(int id);
    }
}


using System.Collections.Generic;
using System.Threading.Tasks;
using SF.Tecnologias.Application.DTOs;

namespace SF.Tecnologias.Application.Services
{
    public interface ICategoriaService
    {
        Task<IEnumerable<CategoriaDto>> ObterTodosAsync(string? busca = null, bool apenasAtivos = false);
        Task<CategoriaDto?> ObterPorIdAsync(int id);
        Task<CategoriaDto> CriarAsync(CriarCategoriaRequest request);
        Task<CategoriaDto> AtualizarAsync(int id, AtualizarCategoriaRequest request);
        Task<bool> InativarAsync(int id);
        Task<bool> ExcluirAsync(int id);
    }
}


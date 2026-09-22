using System.Collections.Generic;
using System.Threading.Tasks;
using SF.Tecnologias.Application.DTOs;

namespace SF.Tecnologias.Application.Services
{
    public interface IProdutoService
    {
        Task<IEnumerable<ProdutoDto>> ObterTodosAsync(string? busca = null, bool apenasAtivos = false);
        Task<ProdutoDto?> ObterPorIdAsync(int id);
        Task<ProdutoDto> CriarAsync(CriarProdutoRequest request);
        Task<ProdutoDto> AtualizarAsync(int id, AtualizarProdutoRequest request);
        Task<bool> InativarAsync(int id);
        Task<bool> ExcluirAsync(int id);
    }
}



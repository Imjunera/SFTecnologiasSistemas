using System.Threading.Tasks;
using SF.Tecnologias.Application.DTOs;

namespace SF.Tecnologias.Application.Services
{
    public interface ICaixaService
    {
        Task<SessaoCaixaDto?> ObterSessaoAbertaAsync();
        Task<SessaoCaixaDto> AbrirAsync(AbrirCaixaRequest request);
        Task<SessaoCaixaDto> FecharAsync(int sessaoId, FecharCaixaRequest request);
    }
}

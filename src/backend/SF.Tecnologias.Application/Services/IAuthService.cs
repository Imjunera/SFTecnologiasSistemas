using System.Threading.Tasks;

namespace SF.Tecnologias.Application.Services
{
    public interface IAuthService
    {
        Task<LoginResponse?> LoginAsync(LoginRequest request);
    }
}

namespace SF.Tecnologias.Application.Services;

public class LoginRequest
{
    public string EmpresaCodigo { get; set; } = default!;
    public string Senha { get; set; } = default!;
    public string? Email { get; set; }
}

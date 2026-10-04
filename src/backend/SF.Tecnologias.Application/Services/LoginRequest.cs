namespace SF.Tecnologias.Application.Services;

/// <summary>
/// Contrato de login da plataforma: o acesso e definido pela empresa + senha.
/// Nao existe mais campo de e-mail no login.
/// </summary>
public class LoginRequest
{
    public string EmpresaCodigo { get; set; } = default!;
    public string Senha { get; set; } = default!;
}

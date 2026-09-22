using System;

namespace SF.Tecnologias.Application.DTOs
{
    public class ClienteDto
    {
        public int Id { get; set; }
        public int EmpresaId { get; set; }
        public string Nome { get; set; } = string.Empty;
        public string? Telefone { get; set; }
        public string? Cpf { get; set; }
        public string? Email { get; set; }
        public string? Endereco { get; set; }
        public string? Observacoes { get; set; }
        public bool Ativo { get; set; }
        public DateTime CriadoEm { get; set; }
        public DateTime? AtualizadoEm { get; set; }
    }

    public class CriarClienteRequest
    {
        public string Nome { get; set; } = string.Empty;
        public string? Telefone { get; set; }
        public string? Cpf { get; set; }
        public string? Email { get; set; }
        public string? Endereco { get; set; }
        public string? Observacoes { get; set; }
    }

    public class AtualizarClienteRequest
    {
        public string Nome { get; set; } = string.Empty;
        public string? Telefone { get; set; }
        public string? Cpf { get; set; }
        public string? Email { get; set; }
        public string? Endereco { get; set; }
        public string? Observacoes { get; set; }
        public bool Ativo { get; set; } = true;
    }
}

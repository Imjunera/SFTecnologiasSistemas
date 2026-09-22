using System;

namespace SF.Tecnologias.Application.DTOs
{
    public class CategoriaDto
    {
        public int Id { get; set; }
        public int EmpresaId { get; set; }
        public string Nome { get; set; } = string.Empty;
        public string? Descricao { get; set; }
        public int Ordem { get; set; }
        public bool Ativo { get; set; }
        public DateTime CriadoEm { get; set; }
        public DateTime? AtualizadoEm { get; set; }
    }

    public class CriarCategoriaRequest
    {
        public string Nome { get; set; } = string.Empty;
        public string? Descricao { get; set; }
        public int? Ordem { get; set; }
    }

    public class AtualizarCategoriaRequest
    {
        public string Nome { get; set; } = string.Empty;
        public string? Descricao { get; set; }
        public int? Ordem { get; set; }
        public bool Ativo { get; set; } = true;
    }
}

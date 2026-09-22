using System;

namespace SF.Tecnologias.Application.DTOs
{
    public class ProdutoDto
    {
        public int Id { get; set; }
        public int EmpresaId { get; set; }
        public int? CategoriaId { get; set; }
        public string? CategoriaNome { get; set; }
        public string Nome { get; set; } = string.Empty;
        public string? Descricao { get; set; }
        public string Codigo { get; set; } = string.Empty;
        public decimal PrecoVenda { get; set; }
        public decimal? PrecoCusto { get; set; }
        public bool Ativo { get; set; }
        public DateTime CriadoEm { get; set; }
        public DateTime? AtualizadoEm { get; set; }
    }

    public class CriarProdutoRequest
    {
        public int? CategoriaId { get; set; }
        public string Nome { get; set; } = string.Empty;
        public string? Descricao { get; set; }
        public string Codigo { get; set; } = string.Empty;
        public decimal PrecoVenda { get; set; }
        public decimal? PrecoCusto { get; set; }
    }

    public class AtualizarProdutoRequest
    {
        public int? CategoriaId { get; set; }
        public string Nome { get; set; } = string.Empty;
        public string? Descricao { get; set; }
        public string Codigo { get; set; } = string.Empty;
        public decimal PrecoVenda { get; set; }
        public decimal? PrecoCusto { get; set; }
        public bool Ativo { get; set; } = true;
    }
}


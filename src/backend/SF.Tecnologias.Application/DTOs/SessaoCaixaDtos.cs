using System;

namespace SF.Tecnologias.Application.DTOs
{
    public class SessaoCaixaDto
    {
        public int Id { get; set; }
        public int EmpresaId { get; set; }
        public int UsuarioId { get; set; }
        public string UsuarioNome { get; set; } = string.Empty;
        public int? MesaId { get; set; }
        public int? MesaNumero { get; set; }
        public DateTime DataAbertura { get; set; }
        public decimal ValorInicial { get; set; }
        public DateTime? DataFechamento { get; set; }
        public decimal? ValorFechamento { get; set; }
        public string Status { get; set; } = "Aberta";

        /// <summary>Vendas concluidas vinculadas a esta sessao (usado no fechamento).</summary>
        public decimal TotalVendas { get; set; }
        public int QuantidadeVendas { get; set; }
    }

    public class AbrirCaixaRequest
    {
        public decimal ValorInicial { get; set; }
        public int? MesaId { get; set; }
    }

    public class FecharCaixaRequest
    {
        public decimal ValorFechamento { get; set; }
    }
}

using System;
using System.Collections.Generic;

namespace SF.Tecnologias.Application.DTOs
{
    public class VendaItemDto
    {
        public int Id { get; set; }
        public int? ProdutoId { get; set; }
        public string ProdutoNome { get; set; } = string.Empty;
        public int Quantidade { get; set; }
        public decimal PrecoUnitario { get; set; }
        public decimal Subtotal { get; set; }
    }

    public class VendaDto
    {
        public int Id { get; set; }
        public int EmpresaId { get; set; }
        public int? UsuarioId { get; set; }
        public string? UsuarioNome { get; set; }
        public int? SessaoCaixaId { get; set; }
        public int? ClienteId { get; set; }
        public string? ClienteNome { get; set; }
        public DateTime DataVenda { get; set; }
        public string FormaPagamento { get; set; } = "dinheiro";
        public decimal ValorTotal { get; set; }
        public string Status { get; set; } = "Concluida";
        public int QuantidadeItens { get; set; }
        public List<VendaItemDto>? Itens { get; set; }
    }

    public class CriarVendaItemRequest
    {
        public int ProdutoId { get; set; }
        public int Quantidade { get; set; } = 1;
    }

    public class CriarVendaRequest
    {
        public int? SessaoCaixaId { get; set; }
        public int? ClienteId { get; set; }
        public string FormaPagamento { get; set; } = "dinheiro";
        public List<CriarVendaItemRequest>? Itens { get; set; }
    }

    public class AtualizarVendaRequest
    {
        public string Status { get; set; } = "Concluida";
    }

    public class ResumoFormaPagamentoDto
    {
        public string FormaPagamento { get; set; } = string.Empty;
        public int Quantidade { get; set; }
        public decimal Total { get; set; }
    }

    public class ResumoCaixaDto
    {
        public int SessaoId { get; set; }
        public int? MesaId { get; set; }
        public int? MesaNumero { get; set; }
        public DateTime DataAbertura { get; set; }
        public decimal ValorInicial { get; set; }
        public int QuantidadeVendas { get; set; }
        public decimal TotalVendas { get; set; }
        public decimal ValorEsperado { get; set; }
        public List<ResumoFormaPagamentoDto> PorFormaPagamento { get; set; } = new();
    }
}

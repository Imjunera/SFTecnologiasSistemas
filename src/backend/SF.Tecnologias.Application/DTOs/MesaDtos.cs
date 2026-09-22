using System;

namespace SF.Tecnologias.Application.DTOs
{
    public class MesaDto
    {
        public int Id { get; set; }
        public int EmpresaId { get; set; }
        public int Numero { get; set; }
        public string? Descricao { get; set; }
        public int Capacidade { get; set; }
        public string Status { get; set; } = "Livre";
        public bool Ativo { get; set; }
        public DateTime CriadoEm { get; set; }
        public DateTime? AtualizadoEm { get; set; }
    }

    public class CriarMesaRequest
    {
        public int Numero { get; set; }
        public string? Descricao { get; set; }
        public int? Capacidade { get; set; }
    }

    public class AtualizarMesaRequest
    {
        public int Numero { get; set; }
        public string? Descricao { get; set; }
        public int? Capacidade { get; set; }
        public string Status { get; set; } = "Livre";
        public bool Ativo { get; set; } = true;
    }
}

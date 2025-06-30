using System;

namespace ConsoleApp1.Models
{
    public class Transaccion
    {
        public int Id { get; set; }
        public string CuentaOrigen { get; set; } = string.Empty;
        public string CuentaDestino { get; set; }
        public decimal Monto { get; set; }
        public decimal? SaldoAnteriorOrigen { get; set; }
        public decimal? SaldoActualOrigen { get; set; }
        public decimal? SaldoAnteriorDestino { get; set; }
        public decimal? SaldoActualDestino { get; set; }
        public DateTime Fecha { get; set; }
        public string Estado { get; set; } = "EXITOSA";
    }
}
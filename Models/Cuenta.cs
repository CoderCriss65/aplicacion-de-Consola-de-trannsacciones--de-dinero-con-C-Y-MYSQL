using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ConsoleApp1.Models
{
    public class Cuenta
    {
        public int Id { get; set; }
        public string NumeroCuenta { get; set; } = string.Empty;
        public string NombreCliente { get; set; } = string.Empty;
        public decimal Saldo { get; set; }
        public DateTime FechaCreacion { get; set; }
    }
}

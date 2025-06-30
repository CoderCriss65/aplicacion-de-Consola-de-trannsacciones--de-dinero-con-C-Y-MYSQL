using System;
using System.Collections.Generic;
using MySqlConnector;

namespace SistemaBancario
{
    class Program
    {
        // Clase para representar una cuenta
        public class Cuenta
        {
            public int Id { get; set; }
            public string NumeroCuenta { get; set; }
            public string NombreCliente { get; set; }
            public decimal Saldo { get; set; }
            public DateTime FechaCreacion { get; set; }
            public bool Activa { get; set; }

            public override string ToString()
            {
                return $"Cuenta: {NumeroCuenta} | Cliente: {NombreCliente} | Saldo: ${Saldo:N2}";
            }
        }

        // Clase para representar una transacción
        public class Transaccion
        {
            public int Id { get; set; }
            public string TipoTransaccion { get; set; }
            public string CuentaOrigen { get; set; }
            public string CuentaDestino { get; set; }
            public decimal Monto { get; set; }
            public decimal? SaldoAnteriorOrigen { get; set; }
            public decimal? SaldoActualOrigen { get; set; }
            public decimal? SaldoAnteriorDestino { get; set; }
            public decimal? SaldoActualDestino { get; set; }
            public string Descripcion { get; set; }
            public DateTime Fecha { get; set; }
            public string Estado { get; set; }
            public string MensajeError { get; set; }
        }

        // Clase para manejar la conexión a la base de datos
        public class DatabaseManager
        {
            private readonly string connectionString;

            public DatabaseManager(string connectionString)
            {
                this.connectionString = connectionString;
            }

            public MySqlConnection GetConnection()
            {
                return new MySqlConnection(connectionString);
            }

            public bool TestConnection()
            {
                try
                {
                    using (var connection = GetConnection())
                    {
                        connection.Open();
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error de conexión: {ex.Message}");
                    return false;
                }
            }
        }

        // Clase principal para operaciones bancarias
        public class BancoService
        {
            private readonly DatabaseManager dbManager;

            public BancoService(DatabaseManager dbManager)
            {
                this.dbManager = dbManager;
            }

            // Crear nueva cuenta
            public bool CrearCuenta(string numeroCuenta, string nombreCliente, decimal saldoInicial = 0)
            {
                using (var connection = dbManager.GetConnection())
                {
                    MySqlTransaction transaction = null;
                    try
                    {
                        connection.Open();
                        transaction = connection.BeginTransaction();

                        // Verificar si la cuenta ya existe
                        string verificarSql = "SELECT COUNT(*) FROM cuentas WHERE numero_cuenta = @numero_cuenta";
                        using (var cmd = new MySqlCommand(verificarSql, connection, transaction))
                        {
                            cmd.Parameters.AddWithValue("@numero_cuenta", numeroCuenta);
                            long count = Convert.ToInt64(cmd.ExecuteScalar());
                            if (count > 0)
                            {
                                Console.WriteLine($"Error: La cuenta {numeroCuenta} ya existe.");
                                transaction.Rollback();
                                return false;
                            }
                        }

                        // Insertar nueva cuenta
                        string insertSql = @"INSERT INTO cuentas (numero_cuenta, nombre_cliente, saldo) 
                                           VALUES (@numero_cuenta, @nombre_cliente, @saldo)";
                        using (var cmd = new MySqlCommand(insertSql, connection, transaction))
                        {
                            cmd.Parameters.AddWithValue("@numero_cuenta", numeroCuenta);
                            cmd.Parameters.AddWithValue("@nombre_cliente", nombreCliente);
                            cmd.Parameters.AddWithValue("@saldo", saldoInicial);
                            cmd.ExecuteNonQuery();
                        }

                        // Registrar transacción si hay saldo inicial
                        if (saldoInicial > 0)
                        {
                            RegistrarTransaccion(connection, transaction, "DEPOSITO", null, numeroCuenta,
                                               saldoInicial, null, null, 0, saldoInicial,
                                               "Depósito inicial al crear cuenta", "EXITOSA", null);
                        }

                        transaction.Commit();
                        Console.WriteLine($"Cuenta {numeroCuenta} creada exitosamente para {nombreCliente}");
                        return true;
                    }
                    catch (Exception ex)
                    {
                        transaction?.Rollback();
                        Console.WriteLine($"Error al crear cuenta: {ex.Message}");
                        return false;
                    }
                }
            }

            // Eliminar cuenta (marcar como inactiva)
            public bool EliminarCuenta(string numeroCuenta)
            {
                using (var connection = dbManager.GetConnection())
                {
                    MySqlTransaction transaction = null;
                    try
                    {
                        connection.Open();
                        transaction = connection.BeginTransaction();

                        // Verificar que la cuenta existe y obtener saldo
                        decimal saldo = 0;
                        string verificarSql = "SELECT saldo FROM cuentas WHERE numero_cuenta = @numero_cuenta AND activa = 1";
                        using (var cmd = new MySqlCommand(verificarSql, connection, transaction))
                        {
                            cmd.Parameters.AddWithValue("@numero_cuenta", numeroCuenta);
                            var result = cmd.ExecuteScalar();
                            if (result == null || result == DBNull.Value)
                            {
                                Console.WriteLine($"Error: La cuenta {numeroCuenta} no existe o ya está inactiva.");
                                transaction.Rollback();
                                return false;
                            }
                            saldo = Convert.ToDecimal(result);
                        }

                        if (saldo > 0)
                        {
                            Console.WriteLine($"Error: No se puede eliminar la cuenta {numeroCuenta}. Saldo actual: ${saldo:N2}");
                            transaction.Rollback();
                            return false;
                        }

                        // Marcar cuenta como inactiva
                        string updateSql = "UPDATE cuentas SET activa = 0 WHERE numero_cuenta = @numero_cuenta";
                        using (var cmd = new MySqlCommand(updateSql, connection, transaction))
                        {
                            cmd.Parameters.AddWithValue("@numero_cuenta", numeroCuenta);
                            cmd.ExecuteNonQuery();
                        }

                        transaction.Commit();
                        Console.WriteLine($"Cuenta {numeroCuenta} eliminada exitosamente");
                        return true;
                    }
                    catch (Exception ex)
                    {
                        transaction?.Rollback();
                        Console.WriteLine($"Error al eliminar cuenta: {ex.Message}");
                        return false;
                    }
                }
            }

            // Depositar dinero
            public bool Depositar(string numeroCuenta, decimal monto)
            {
                if (monto <= 0)
                {
                    Console.WriteLine("Error: El monto debe ser mayor a cero.");
                    return false;
                }

                using (var connection = dbManager.GetConnection())
                {
                    MySqlTransaction transaction = null;
                    try
                    {
                        connection.Open();
                        transaction = connection.BeginTransaction();

                        // Obtener saldo actual con bloqueo
                        decimal saldoAnterior;
                        string obtenerSaldoSql = "SELECT saldo FROM cuentas WHERE numero_cuenta = @numero_cuenta AND activa = 1 FOR UPDATE";
                        using (var cmd = new MySqlCommand(obtenerSaldoSql, connection, transaction))
                        {
                            cmd.Parameters.AddWithValue("@numero_cuenta", numeroCuenta);
                            var result = cmd.ExecuteScalar();
                            if (result == null || result == DBNull.Value)
                            {
                                Console.WriteLine($"Error: La cuenta {numeroCuenta} no existe o está inactiva.");
                                transaction.Rollback();
                                return false;
                            }
                            saldoAnterior = Convert.ToDecimal(result);
                        }

                        decimal saldoNuevo = saldoAnterior + monto;

                        // Actualizar saldo
                        string actualizarSql = "UPDATE cuentas SET saldo = @saldo_nuevo WHERE numero_cuenta = @numero_cuenta";
                        using (var cmd = new MySqlCommand(actualizarSql, connection, transaction))
                        {
                            cmd.Parameters.AddWithValue("@saldo_nuevo", saldoNuevo);
                            cmd.Parameters.AddWithValue("@numero_cuenta", numeroCuenta);
                            cmd.ExecuteNonQuery();
                        }

                        // Registrar transacción
                        RegistrarTransaccion(connection, transaction, "DEPOSITO", null, numeroCuenta,
                                           monto, null, null, saldoAnterior, saldoNuevo,
                                           $"Depósito de ${monto:N2}", "EXITOSA", null);

                        transaction.Commit();
                        Console.WriteLine($"Depósito exitoso. Saldo anterior: ${saldoAnterior:N2}, Saldo actual: ${saldoNuevo:N2}");
                        return true;
                    }
                    catch (Exception ex)
                    {
                        transaction?.Rollback();
                        Console.WriteLine($"Error al depositar: {ex.Message}");
                        return false;
                    }
                }
            }

            // Retirar dinero
            public bool Retirar(string numeroCuenta, decimal monto)
            {
                if (monto <= 0)
                {
                    Console.WriteLine("Error: El monto debe ser mayor a cero.");
                    return false;
                }

                using (var connection = dbManager.GetConnection())
                {
                    MySqlTransaction transaction = null;
                    try
                    {
                        connection.Open();
                        transaction = connection.BeginTransaction();

                        // Obtener saldo actual con bloqueo
                        decimal saldoAnterior;
                        string obtenerSaldoSql = "SELECT saldo FROM cuentas WHERE numero_cuenta = @numero_cuenta AND activa = 1 FOR UPDATE";
                        using (var cmd = new MySqlCommand(obtenerSaldoSql, connection, transaction))
                        {
                            cmd.Parameters.AddWithValue("@numero_cuenta", numeroCuenta);
                            var result = cmd.ExecuteScalar();
                            if (result == null || result == DBNull.Value)
                            {
                                Console.WriteLine($"Error: La cuenta {numeroCuenta} no existe o está inactiva.");
                                transaction.Rollback();
                                return false;
                            }
                            saldoAnterior = Convert.ToDecimal(result);
                        }

                        if (saldoAnterior < monto)
                        {
                            Console.WriteLine($"Error: Saldo insuficiente. Saldo actual: ${saldoAnterior:N2}");
                            RegistrarTransaccion(connection, transaction, "RETIRO", numeroCuenta, null,
                                               monto, saldoAnterior, saldoAnterior, null, null,
                                               $"Intento de retiro de ${monto:N2}", "FALLIDA", "Saldo insuficiente");
                            transaction.Commit(); // Confirmar registro de transacción fallida
                            return false;
                        }

                        decimal saldoNuevo = saldoAnterior - monto;

                        // Actualizar saldo
                        string actualizarSql = "UPDATE cuentas SET saldo = @saldo_nuevo WHERE numero_cuenta = @numero_cuenta";
                        using (var cmd = new MySqlCommand(actualizarSql, connection, transaction))
                        {
                            cmd.Parameters.AddWithValue("@saldo_nuevo", saldoNuevo);
                            cmd.Parameters.AddWithValue("@numero_cuenta", numeroCuenta);
                            cmd.ExecuteNonQuery();
                        }

                        // Registrar transacción
                        RegistrarTransaccion(connection, transaction, "RETIRO", numeroCuenta, null,
                                           monto, saldoAnterior, saldoNuevo, null, null,
                                           $"Retiro de ${monto:N2}", "EXITOSA", null);

                        transaction.Commit();
                        Console.WriteLine($"Retiro exitoso. Saldo anterior: ${saldoAnterior:N2}, Saldo actual: ${saldoNuevo:N2}");
                        return true;
                    }
                    catch (Exception ex)
                    {
                        transaction?.Rollback();
                        Console.WriteLine($"Error al retirar: {ex.Message}");
                        return false;
                    }
                }
            }

            // Transferir dinero entre cuentas
            public bool Transferir(string cuentaOrigen, string cuentaDestino, decimal monto)
            {
                if (monto <= 0)
                {
                    Console.WriteLine("Error: El monto debe ser mayor a cero.");
                    return false;
                }

                if (cuentaOrigen == cuentaDestino)
                {
                    Console.WriteLine("Error: No se puede transferir a la misma cuenta.");
                    return false;
                }

                using (var connection = dbManager.GetConnection())
                {
                    MySqlTransaction transaction = null;
                    try
                    {
                        connection.Open();
                        transaction = connection.BeginTransaction();

                        // Obtener saldos con bloqueo (orden alfabético para evitar deadlocks)
                        string primeraCuenta = string.Compare(cuentaOrigen, cuentaDestino) < 0 ? cuentaOrigen : cuentaDestino;
                        string segundaCuenta = string.Compare(cuentaOrigen, cuentaDestino) < 0 ? cuentaDestino : cuentaOrigen;

                        decimal saldoOrigenAnterior = 0, saldoDestinoAnterior = 0;

                        // Bloquear primera cuenta
                        string obtenerSaldo1Sql = "SELECT saldo FROM cuentas WHERE numero_cuenta = @numero_cuenta AND activa = 1 FOR UPDATE";
                        using (var cmd = new MySqlCommand(obtenerSaldo1Sql, connection, transaction))
                        {
                            cmd.Parameters.AddWithValue("@numero_cuenta", primeraCuenta);
                            var result = cmd.ExecuteScalar();
                            if (result == null || result == DBNull.Value)
                            {
                                Console.WriteLine($"Error: La cuenta {primeraCuenta} no existe o está inactiva.");
                                transaction.Rollback();
                                return false;
                            }
                            if (primeraCuenta == cuentaOrigen)
                                saldoOrigenAnterior = Convert.ToDecimal(result);
                            else
                                saldoDestinoAnterior = Convert.ToDecimal(result);
                        }

                        // Bloquear segunda cuenta
                        string obtenerSaldo2Sql = "SELECT saldo FROM cuentas WHERE numero_cuenta = @numero_cuenta AND activa = 1 FOR UPDATE";
                        using (var cmd = new MySqlCommand(obtenerSaldo2Sql, connection, transaction))
                        {
                            cmd.Parameters.AddWithValue("@numero_cuenta", segundaCuenta);
                            var result = cmd.ExecuteScalar();
                            if (result == null || result == DBNull.Value)
                            {
                                Console.WriteLine($"Error: La cuenta {segundaCuenta} no existe o está inactiva.");
                                transaction.Rollback();
                                return false;
                            }
                            if (segundaCuenta == cuentaOrigen)
                                saldoOrigenAnterior = Convert.ToDecimal(result);
                            else
                                saldoDestinoAnterior = Convert.ToDecimal(result);
                        }

                        // Verificar saldo suficiente
                        if (saldoOrigenAnterior < monto)
                        {
                            Console.WriteLine($"Error: Saldo insuficiente en cuenta origen. Saldo actual: ${saldoOrigenAnterior:N2}");
                            RegistrarTransaccion(connection, transaction, "TRANSFERENCIA", cuentaOrigen, cuentaDestino,
                                               monto, saldoOrigenAnterior, saldoOrigenAnterior, saldoDestinoAnterior, saldoDestinoAnterior,
                                               $"Intento de transferencia de ${monto:N2}", "FALLIDA", "Saldo insuficiente");
                            transaction.Commit(); // Confirmar registro de transacción fallida
                            return false;
                        }

                        decimal saldoOrigenNuevo = saldoOrigenAnterior - monto;
                        decimal saldoDestinoNuevo = saldoDestinoAnterior + monto;

                        // Actualizar cuenta origen
                        string actualizarOrigenSql = "UPDATE cuentas SET saldo = @saldo_nuevo WHERE numero_cuenta = @numero_cuenta";
                        using (var cmd = new MySqlCommand(actualizarOrigenSql, connection, transaction))
                        {
                            cmd.Parameters.AddWithValue("@saldo_nuevo", saldoOrigenNuevo);
                            cmd.Parameters.AddWithValue("@numero_cuenta", cuentaOrigen);
                            cmd.ExecuteNonQuery();
                        }

                        // Actualizar cuenta destino
                        string actualizarDestinoSql = "UPDATE cuentas SET saldo = @saldo_nuevo WHERE numero_cuenta = @numero_cuenta";
                        using (var cmd = new MySqlCommand(actualizarDestinoSql, connection, transaction))
                        {
                            cmd.Parameters.AddWithValue("@saldo_nuevo", saldoDestinoNuevo);
                            cmd.Parameters.AddWithValue("@numero_cuenta", cuentaDestino);
                            cmd.ExecuteNonQuery();
                        }

                        // Registrar transacción
                        RegistrarTransaccion(connection, transaction, "TRANSFERENCIA", cuentaOrigen, cuentaDestino,
                                           monto, saldoOrigenAnterior, saldoOrigenNuevo, saldoDestinoAnterior, saldoDestinoNuevo,
                                           $"Transferencia de ${monto:N2} de {cuentaOrigen} a {cuentaDestino}", "EXITOSA", null);

                        transaction.Commit();
                        Console.WriteLine($"¡TRANSFERENCIA REALIZADA DE MANERA EXITOSA!....");
                        Console.WriteLine($"**************************************************");
                        Console.WriteLine($"Transferencia exitosa de ${monto:N2} de {cuentaOrigen} a {cuentaDestino}");
                        Console.WriteLine($"Saldo {cuentaOrigen}: ${saldoOrigenAnterior:N2} -> ${saldoOrigenNuevo:N2}");
                        Console.WriteLine($"Saldo {cuentaDestino}: ${saldoDestinoAnterior:N2} -> ${saldoDestinoNuevo:N2}");
                        return true;
                    }
                    catch (Exception ex)
                    {
                        transaction?.Rollback();
                        Console.WriteLine($"Error al transferir: {ex.Message}");
                        return false;
                    }
                }
            }

            // Método auxiliar para registrar transacciones
            private void RegistrarTransaccion(MySqlConnection connection, MySqlTransaction transaction,
                                            string tipo, string cuentaOrigen, string cuentaDestino, decimal monto,
                                            decimal? saldoAnteriorOrigen, decimal? saldoActualOrigen,
                                            decimal? saldoAnteriorDestino, decimal? saldoActualDestino,
                                            string descripcion, string estado, string mensajeError)
            {
                string insertSql = @"INSERT INTO transacciones 
                                   (tipo_transaccion, cuenta_origen, cuenta_destino, monto, 
                                    saldo_anterior_origen, saldo_actual_origen, saldo_anterior_destino, saldo_actual_destino,
                                    descripcion, estado, mensaje_error) 
                                   VALUES 
                                   (@tipo, @cuenta_origen, @cuenta_destino, @monto, 
                                    @saldo_anterior_origen, @saldo_actual_origen, @saldo_anterior_destino, @saldo_actual_destino,
                                    @descripcion, @estado, @mensaje_error)";

                using (var cmd = new MySqlCommand(insertSql, connection, transaction))
                {
                    cmd.Parameters.AddWithValue("@tipo", tipo);
                    cmd.Parameters.AddWithValue("@cuenta_origen", (object)cuentaOrigen ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@cuenta_destino", (object)cuentaDestino ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@monto", monto);
                    cmd.Parameters.AddWithValue("@saldo_anterior_origen", (object)saldoAnteriorOrigen ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@saldo_actual_origen", (object)saldoActualOrigen ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@saldo_anterior_destino", (object)saldoAnteriorDestino ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@saldo_actual_destino", (object)saldoActualDestino ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@descripcion", descripcion);
                    cmd.Parameters.AddWithValue("@estado", estado);
                    cmd.Parameters.AddWithValue("@mensaje_error", (object)mensajeError ?? DBNull.Value);
                    cmd.ExecuteNonQuery();
                }
            }

            // Consultar todas las cuentas
            public List<Cuenta> ObtenerCuentas()
            {
                var cuentas = new List<Cuenta>();
                using (var connection = dbManager.GetConnection())
                {
                    try
                    {
                        connection.Open();
                        string sql = "SELECT * FROM cuentas WHERE activa = 1 ORDER BY numero_cuenta";
                        using (var cmd = new MySqlCommand(sql, connection))
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                cuentas.Add(new Cuenta
                                {
                                    Id = reader.GetInt32("id"),
                                    NumeroCuenta = reader.GetString("numero_cuenta"),
                                    NombreCliente = reader.GetString("nombre_cliente"),
                                    Saldo = reader.GetDecimal("saldo"),
                                    FechaCreacion = reader.GetDateTime("fecha_creacion"),
                                    Activa = reader.GetBoolean("activa")
                                });
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error al obtener cuentas: {ex.Message}");
                    }
                }
                return cuentas;
            }

            // Consultar historial de transacciones
            public List<Transaccion> ObtenerTransacciones(string numeroCuenta = null, int limite = 50)
            {
                var transacciones = new List<Transaccion>();
                using (var connection = dbManager.GetConnection())
                {
                    try
                    {
                        connection.Open();
                        string sql = @"SELECT * FROM transacciones 
                                     WHERE (@numero_cuenta IS NULL OR cuenta_origen = @numero_cuenta OR cuenta_destino = @numero_cuenta)
                                     ORDER BY fecha DESC LIMIT @limite";
                        using (var cmd = new MySqlCommand(sql, connection))
                        {
                            cmd.Parameters.AddWithValue("@numero_cuenta", (object)numeroCuenta ?? DBNull.Value);
                            cmd.Parameters.AddWithValue("@limite", limite);
                            using (var reader = cmd.ExecuteReader())
                            {
                                while (reader.Read())
                                {
                                    transacciones.Add(new Transaccion
                                    {
                                        Id = reader.GetInt32("id"),
                                        TipoTransaccion = reader.GetString("tipo_transaccion"),
                                        CuentaOrigen = reader.IsDBNull(reader.GetOrdinal("cuenta_origen")) ? null : reader.GetString("cuenta_origen"),
                                        CuentaDestino = reader.IsDBNull(reader.GetOrdinal("cuenta_destino")) ? null : reader.GetString("cuenta_destino"),
                                        Monto = reader.GetDecimal("monto"),
                                        SaldoAnteriorOrigen = reader.IsDBNull(reader.GetOrdinal("saldo_anterior_origen")) ? (decimal?)null : reader.GetDecimal("saldo_anterior_origen"),
                                        SaldoActualOrigen = reader.IsDBNull(reader.GetOrdinal("saldo_actual_origen")) ? (decimal?)null : reader.GetDecimal("saldo_actual_origen"),
                                        SaldoAnteriorDestino = reader.IsDBNull(reader.GetOrdinal("saldo_anterior_destino")) ? (decimal?)null : reader.GetDecimal("saldo_anterior_destino"),
                                        SaldoActualDestino = reader.IsDBNull(reader.GetOrdinal("saldo_actual_destino")) ? (decimal?)null : reader.GetDecimal("saldo_actual_destino"),
                                        Descripcion = reader.IsDBNull(reader.GetOrdinal("descripcion")) ? null : reader.GetString("descripcion"),
                                        Fecha = reader.GetDateTime("fecha"),
                                        Estado = reader.GetString("estado"),
                                        MensajeError = reader.IsDBNull(reader.GetOrdinal("mensaje_error")) ? null : reader.GetString("mensaje_error")
                                    });
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error al obtener transacciones: {ex.Message}");
                    }
                }
                return transacciones;
            }
        }

        // Clase principal del programa
        static void Main(string[] args)
        {
            Console.WriteLine("=== SISTEMA BANCARIO ===");
            Console.WriteLine();

            // Configurar conexión a la base de datos (AJUSTAR ESTOS VALORES)
            string connectionString = "server=192.168.20.24;user=root;database=banco;port=3306;password=123;SslMode=None";
            var dbManager = new DatabaseManager(connectionString);

            // Probar conexión
            if (!dbManager.TestConnection())
            {
                Console.WriteLine("No se pudo conectar a la base de datos. Verifique la configuración.");
                Console.WriteLine("Presione cualquier tecla para salir...");
                Console.ReadKey();
                return;
            }

            var bancoService = new BancoService(dbManager);
            Console.WriteLine("Conexión exitosa a la base de datos.\n");

            // Menú principal
            bool continuar = true;
            while (continuar)
            {
                MostrarMenu();
                string opcion = Console.ReadLine();

                switch (opcion)
                {
                    case "1":
                        CrearCuenta(bancoService);
                        break;
                    case "2":
                        EliminarCuenta(bancoService);
                        break;
                    case "3":
                        Depositar(bancoService);
                        break;
                    case "4":
                        Retirar(bancoService);
                        break;
                    case "5":
                        Transferir(bancoService);
                        break;
                    case "6":
                        ConsultarCuentas(bancoService);
                        break;
                    case "7":
                        ConsultarTransacciones(bancoService);
                        break;
                    case "0":
                        continuar = false;
                        break;
                    default:
                        Console.WriteLine("Opción inválida. Intente de nuevo.");
                        break;
                }

                if (continuar)
                {
                    Console.WriteLine("\nPresione cualquier tecla para continuar...");
                    Console.ReadKey();
                }
            }

            Console.WriteLine("¡Gracias por usar el Sistema Bancario!");
        }

        static void MostrarMenu()
        {
            Console.Clear();
            Console.WriteLine("=== SISTEMA BANCARIO ===");
            Console.WriteLine();
            Console.WriteLine("1. Crear cuenta");
            Console.WriteLine("2. Eliminar cuenta");
            Console.WriteLine("3. Depositar dinero");
            Console.WriteLine("4. Retirar dinero");
            Console.WriteLine("5. Transferir dinero");
            Console.WriteLine("6. Consultar cuentas");
            Console.WriteLine("7. Historial de transacciones");
            Console.WriteLine("0. Salir");
            Console.WriteLine();
            Console.Write("Seleccione una opción: ");
        }

        static void CrearCuenta(BancoService bancoService)
        {
            Console.Clear();
            Console.WriteLine("=== CREAR CUENTA ===");
            Console.WriteLine();

            Console.Write("Número de cuenta: ");
            string numeroCuenta = Console.ReadLine();

            Console.Write("Nombre del cliente: ");
            string nombreCliente = Console.ReadLine();

            Console.Write("Saldo inicial (opcional, presione Enter para 0): ");
            string saldoStr = Console.ReadLine();
            decimal saldoInicial = 0;

            if (!string.IsNullOrEmpty(saldoStr))
            {
                if (!decimal.TryParse(saldoStr, out saldoInicial) || saldoInicial < 0)
                {
                    Console.WriteLine("Saldo inválido. Se establecerá en 0.");
                    saldoInicial = 0;
                }
            }

            bancoService.CrearCuenta(numeroCuenta, nombreCliente, saldoInicial);
        }

        static void EliminarCuenta(BancoService bancoService)
        {
            Console.Clear();
            Console.WriteLine("=== ELIMINAR CUENTA ===");
            Console.WriteLine();

            Console.Write("Número de cuenta a eliminar: ");
            string numeroCuenta = Console.ReadLine();

            Console.Write($"¿Está seguro de eliminar la cuenta {numeroCuenta}? (s/n): ");
            string confirmacion = Console.ReadLine();

            if (confirmacion?.ToLower() == "s")
            {
                bancoService.EliminarCuenta(numeroCuenta);
            }
            else
            {
                Console.WriteLine("Operación cancelada.");
            }
        }

        static void Depositar(BancoService bancoService)
        {
            Console.Clear();
            Console.WriteLine("=== DEPOSITAR DINERO ===");
            Console.WriteLine();

            Console.Write("Número de cuenta: ");
            string numeroCuenta = Console.ReadLine();

            Console.Write("Monto a depositar: $");
            string montoStr = Console.ReadLine();

            if (decimal.TryParse(montoStr, out decimal monto))
            {
                bancoService.Depositar(numeroCuenta, monto);
            }
            else
            {
                Console.WriteLine("Monto inválido.");
            }
        }

        static void Retirar(BancoService bancoService)
        {
            Console.Clear();
            Console.WriteLine("=== RETIRAR DINERO ===");
            Console.WriteLine();

            Console.Write("Número de cuenta: ");
            string numeroCuenta = Console.ReadLine();

            Console.Write("Monto a retirar: $");
            string montoStr = Console.ReadLine();

            if (decimal.TryParse(montoStr, out decimal monto))
            {
                bancoService.Retirar(numeroCuenta, monto);
            }
            else
            {
                Console.WriteLine("Monto inválido.");
            }
        }

        static void Transferir(BancoService bancoService)
        {
            Console.Clear();
            Console.WriteLine("=== TRANSFERIR DINERO ===");
            Console.WriteLine();

            Console.Write("Cuenta origen: ");
            string cuentaOrigen = Console.ReadLine();

            Console.Write("Cuenta destino: ");
            string cuentaDestino = Console.ReadLine();

            Console.Write("Monto a transferir: $");
            string montoStr = Console.ReadLine();

            if (decimal.TryParse(montoStr, out decimal monto))
            {
                bancoService.Transferir(cuentaOrigen, cuentaDestino, monto);
            }
            else
            {
                Console.WriteLine("Monto inválido.");
            }
        }

        static void ConsultarCuentas(BancoService bancoService)
        {
            Console.Clear();
            Console.WriteLine("=== CONSULTAR CUENTAS ===");
            Console.WriteLine();

            var cuentas = bancoService.ObtenerCuentas();

            if (cuentas.Count == 0)
            {
                Console.WriteLine("No hay cuentas registradas.");
                return;
            }

            // Encabezado de la tabla
            Console.WriteLine("+----+--------------------+-----------------------+----------+---------------------+--------+");
            Console.WriteLine("| id | numero_cuenta      | nombre_cliente        | saldo    | fecha_creacion      | activa |");
            Console.WriteLine("+----+--------------------+-----------------------+----------+---------------------+--------+");

            foreach (var cuenta in cuentas)
            {
                Console.WriteLine($"| {cuenta.Id,2} | {cuenta.NumeroCuenta,-18} | {cuenta.NombreCliente,-21} | {cuenta.Saldo,8:N2} | {cuenta.FechaCreacion:yyyy-MM-dd HH:mm:ss} | {(cuenta.Activa ? "1" : "0"),6} |");
            }

            Console.WriteLine("+----+--------------------+-----------------------+----------+---------------------+--------+");
            Console.WriteLine($"Total de cuentas: {cuentas.Count}");
        }

        static void ConsultarTransacciones(BancoService bancoService)
        {
            Console.Clear();
            Console.WriteLine("=== HISTORIAL DE TRANSACCIONES ===");
            Console.WriteLine();

            Console.Write("Número de cuenta (opcional, presione Enter para todas): ");
            string numeroCuenta = Console.ReadLine();

            if (string.IsNullOrEmpty(numeroCuenta))
                numeroCuenta = null;

            Console.Write("Cantidad de registros (presione Enter para 50): ");
            string limiteStr = Console.ReadLine();
            int limite = 50;

            if (!string.IsNullOrEmpty(limiteStr))
            {
                if (!int.TryParse(limiteStr, out limite) || limite <= 0)
                {
                    limite = 50;
                }
            }

            var transacciones = bancoService.ObtenerTransacciones(numeroCuenta, limite);

            if (transacciones.Count == 0)
            {
                Console.WriteLine("No hay transacciones registradas.");
                return;
            }

            // Encabezado de la tabla
            Console.WriteLine("+----+------------------+--------------+--------------+----------+------------------+------------------+------------------+------------------+---------------------+----------+--------------+");
            Console.WriteLine("| id | tipo_transaccion | cuenta_origen| cuenta_destino| monto    | saldo_ant_origen | saldo_act_origen | saldo_ant_destino| saldo_act_destino| fecha               | estado   | mensaje_error|");
            Console.WriteLine("+----+------------------+--------------+--------------+----------+------------------+------------------+------------------+------------------+---------------------+----------+--------------+");

            foreach (var t in transacciones)
            {
                Console.WriteLine($"| {t.Id,2} | {t.TipoTransaccion,-16} | {FormatNull(t.CuentaOrigen),-12} | {FormatNull(t.CuentaDestino),-12} | {t.Monto,8:N2} | {FormatDecimal(t.SaldoAnteriorOrigen),16} | {FormatDecimal(t.SaldoActualOrigen),16} | {FormatDecimal(t.SaldoAnteriorDestino),16} | {FormatDecimal(t.SaldoActualDestino),16} | {t.Fecha:yyyy-MM-dd HH:mm:ss} | {t.Estado,-8} | {FormatNull(t.MensajeError),-12} |");
            }

            Console.WriteLine("+----+------------------+--------------+--------------+----------+------------------+------------------+------------------+------------------+---------------------+----------+--------------+");
            Console.WriteLine($"Total de transacciones: {transacciones.Count}");
        }

        // Métodos auxiliares para formatear valores nulos
        static string FormatNull(string value) => value ?? "NULL";
        static string FormatDecimal(decimal? value) => value.HasValue ? $"{value.Value:N2}" : "NULL";
    }
}
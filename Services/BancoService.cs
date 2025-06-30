using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using ConsoleApp1.Models;

namespace Banco.Services
{
    public class BancoService
    {
        private readonly string _connectionString;

        public BancoService(string connectionString)
        {
            _connectionString = connectionString;
        }

        public void CrearCuenta(Cuenta cuenta)
        {
            try
            {
                using (var conn = new MySqlConnection(_connectionString))
                {
                    conn.Open();
                    var query = "INSERT INTO cuentas (numero_cuenta, nombre_cliente, saldo) VALUES (@numero, @nombre, @saldo)";

                    using (var cmd = new MySqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@numero", cuenta.NumeroCuenta ?? "");
                        cmd.Parameters.AddWithValue("@nombre", cuenta.NombreCliente ?? "");
                        cmd.Parameters.AddWithValue("@saldo", cuenta.Saldo);
                        cmd.ExecuteNonQuery();
                    }
                }
            }
            catch (MySqlException ex)
            {
                if (ex.Number == 1062)
                    throw new Exception("Error: El número de cuenta ya existe");
                throw new Exception($"Error de MySQL ({ex.Number}): {ex.Message}");
            }
            catch (Exception ex)
            {
                throw new Exception($"Error al crear cuenta: {ex.Message}");
            }
        }

        public void EliminarCuenta(string numeroCuenta)
        {
            try
            {
                using (var conn = new MySqlConnection(_connectionString))
                {
                    conn.Open();
                    var query = "DELETE FROM cuentas WHERE numero_cuenta = @numero";

                    using (var cmd = new MySqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@numero", numeroCuenta ?? "");
                        int rowsAffected = cmd.ExecuteNonQuery();

                        if (rowsAffected == 0)
                            throw new Exception("Cuenta no encontrada");
                    }
                }
            }
            catch (MySqlException ex)
            {
                throw new Exception($"Error de MySQL ({ex.Number}): {ex.Message}");
            }
        }

        public bool CuentaExiste(string numeroCuenta)
        {
            try
            {
                using (var conn = new MySqlConnection(_connectionString))
                {
                    conn.Open();
                    var query = "SELECT COUNT(*) FROM cuentas WHERE numero_cuenta = @numero";

                    using (var cmd = new MySqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@numero", numeroCuenta ?? "");
                        object result = cmd.ExecuteScalar();
                        return (result != null && result != DBNull.Value && Convert.ToInt32(result) > 0);
                    }
                }
            }
            catch
            {
                return false;
            }
        }

        public void Depositar(string numeroCuenta, decimal monto)
        {
            numeroCuenta = numeroCuenta?.Trim() ?? "";

            using (var conn = new MySqlConnection(_connectionString))
            {
                conn.Open();
                var transaction = conn.BeginTransaction();

                try
                {
                    if (!CuentaExiste(numeroCuenta))
                        throw new Exception("La cuenta no existe");

                    decimal saldoAnterior = ObtenerSaldo(conn, transaction, numeroCuenta);
                    decimal nuevoSaldo = saldoAnterior + monto;

                    // Actualizar saldo
                    var cmdUpdate = new MySqlCommand(
                        "UPDATE cuentas SET saldo = @nuevoSaldo WHERE numero_cuenta = @numero",
                        conn, transaction);
                    cmdUpdate.Parameters.AddWithValue("@nuevoSaldo", nuevoSaldo);
                    cmdUpdate.Parameters.AddWithValue("@numero", numeroCuenta);
                    cmdUpdate.ExecuteNonQuery();

                    // Registrar transacción (SOLO ORIGEN)
                    var cmdTrans = new MySqlCommand(
                        "INSERT INTO transacciones " +
                        "(cuenta_origen, monto, saldo_anterior_origen, saldo_actual_origen, estado) " +
                        "VALUES (@origen, @monto, @saldoAnt, @saldoAct, 'EXITOSA')",
                        conn, transaction);
                    cmdTrans.Parameters.AddWithValue("@origen", numeroCuenta);
                    cmdTrans.Parameters.AddWithValue("@monto", monto);
                    cmdTrans.Parameters.AddWithValue("@saldoAnt", saldoAnterior);
                    cmdTrans.Parameters.AddWithValue("@saldoAct", nuevoSaldo);
                    cmdTrans.ExecuteNonQuery();

                    transaction.Commit();
                }
                catch (Exception ex)
                {
                    try { transaction.Rollback(); } catch { }
                    throw new Exception($"Error en depósito: {ex.Message}");
                }
            }
        }

        public void Retirar(string numeroCuenta, decimal monto)
        {
            numeroCuenta = numeroCuenta?.Trim() ?? "";

            using (var conn = new MySqlConnection(_connectionString))
            {
                conn.Open();
                var transaction = conn.BeginTransaction();

                try
                {
                    if (!CuentaExiste(numeroCuenta))
                        throw new Exception("La cuenta no existe");

                    decimal saldoAnterior = ObtenerSaldo(conn, transaction, numeroCuenta);

                    if (saldoAnterior < monto)
                        throw new Exception("Saldo insuficiente");

                    decimal nuevoSaldo = saldoAnterior - monto;

                    // Actualizar saldo
                    var cmdUpdate = new MySqlCommand(
                        "UPDATE cuentas SET saldo = @nuevoSaldo WHERE numero_cuenta = @numero",
                        conn, transaction);
                    cmdUpdate.Parameters.AddWithValue("@nuevoSaldo", nuevoSaldo);
                    cmdUpdate.Parameters.AddWithValue("@numero", numeroCuenta);
                    cmdUpdate.ExecuteNonQuery();

                    // Registrar transacción (SOLO ORIGEN)
                    var cmdTrans = new MySqlCommand(
                        "INSERT INTO transacciones " +
                        "(cuenta_origen, monto, saldo_anterior_origen, saldo_actual_origen, estado) " +
                        "VALUES (@origen, @monto, @saldoAnt, @saldoAct, 'EXITOSA')",
                        conn, transaction);
                    cmdTrans.Parameters.AddWithValue("@origen", numeroCuenta);
                    cmdTrans.Parameters.AddWithValue("@monto", monto);
                    cmdTrans.Parameters.AddWithValue("@saldoAnt", saldoAnterior);
                    cmdTrans.Parameters.AddWithValue("@saldoAct", nuevoSaldo);
                    cmdTrans.ExecuteNonQuery();

                    transaction.Commit();
                }
                catch (Exception ex)
                {
                    try { transaction.Rollback(); } catch { }
                    throw new Exception($"Error en retiro: {ex.Message}");
                }
            }
        }

        public void Transferir(string cuentaOrigen, string cuentaDestino, decimal monto)
        {
            cuentaOrigen = cuentaOrigen?.Trim() ?? "";
            cuentaDestino = cuentaDestino?.Trim() ?? "";

            using (var conn = new MySqlConnection(_connectionString))
            {
                conn.Open();
                var transaction = conn.BeginTransaction();

                try
                {
                    if (!CuentaExiste(cuentaOrigen))
                        throw new Exception("Cuenta origen no existe");
                    if (!CuentaExiste(cuentaDestino))
                        throw new Exception("Cuenta destino no existe");

                    // Obtener saldos con bloqueo
                    decimal saldoAnteriorOrigen = ObtenerSaldo(conn, transaction, cuentaOrigen);
                    decimal saldoAnteriorDestino = ObtenerSaldo(conn, transaction, cuentaDestino);

                    if (saldoAnteriorOrigen < monto)
                        throw new Exception("Saldo insuficiente en cuenta origen");

                    // Calcular nuevos saldos
                    decimal nuevoSaldoOrigen = saldoAnteriorOrigen - monto;
                    decimal nuevoSaldoDestino = saldoAnteriorDestino + monto;

                    // Actualizar origen
                    var cmdUpdateOrigen = new MySqlCommand(
                        "UPDATE cuentas SET saldo = @nuevoSaldo WHERE numero_cuenta = @cuenta",
                        conn, transaction);
                    cmdUpdateOrigen.Parameters.AddWithValue("@nuevoSaldo", nuevoSaldoOrigen);
                    cmdUpdateOrigen.Parameters.AddWithValue("@cuenta", cuentaOrigen);
                    cmdUpdateOrigen.ExecuteNonQuery();

                    // Actualizar destino
                    var cmdUpdateDestino = new MySqlCommand(
                        "UPDATE cuentas SET saldo = @nuevoSaldo WHERE numero_cuenta = @cuenta",
                        conn, transaction);
                    cmdUpdateDestino.Parameters.AddWithValue("@nuevoSaldo", nuevoSaldoDestino);
                    cmdUpdateDestino.Parameters.AddWithValue("@cuenta", cuentaDestino);
                    cmdUpdateDestino.ExecuteNonQuery();

                    // Registrar transacción
                    var cmdTrans = new MySqlCommand(
                        "INSERT INTO transacciones " +
                        "(cuenta_origen, cuenta_destino, monto, " +
                        "saldo_anterior_origen, saldo_actual_origen, " +
                        "saldo_anterior_destino, saldo_actual_destino, estado) " +
                        "VALUES (@origen, @destino, @monto, " +
                        "@saldoAntOrigen, @saldoActOrigen, " +
                        "@saldoAntDestino, @saldoActDestino, 'EXITOSA')",
                        conn, transaction);

                    cmdTrans.Parameters.AddWithValue("@origen", cuentaOrigen);
                    cmdTrans.Parameters.AddWithValue("@destino", cuentaDestino);
                    cmdTrans.Parameters.AddWithValue("@monto", monto);
                    cmdTrans.Parameters.AddWithValue("@saldoAntOrigen", saldoAnteriorOrigen);
                    cmdTrans.Parameters.AddWithValue("@saldoActOrigen", nuevoSaldoOrigen);
                    cmdTrans.Parameters.AddWithValue("@saldoAntDestino", saldoAnteriorDestino);
                    cmdTrans.Parameters.AddWithValue("@saldoActDestino", nuevoSaldoDestino);
                    cmdTrans.ExecuteNonQuery();

                    transaction.Commit();
                }
                catch (Exception ex)
                {
                    try
                    {
                        // Registrar transacción fallida
                        var cmdTrans = new MySqlCommand(
                            "INSERT INTO transacciones " +
                            "(cuenta_origen, cuenta_destino, monto, estado) " +
                            "VALUES (@origen, @destino, @monto, 'FALLIDA')",
                            conn);
                        cmdTrans.Parameters.AddWithValue("@origen", cuentaOrigen);
                        cmdTrans.Parameters.AddWithValue("@destino", cuentaDestino);
                        cmdTrans.Parameters.AddWithValue("@monto", monto);
                        cmdTrans.ExecuteNonQuery();
                    }
                    catch { }

                    try { transaction.Rollback(); } catch { }
                    throw new Exception($"Error en transferencia: {ex.Message}");
                }
            }
        }

        public decimal ObtenerSaldo(string numeroCuenta)
        {
            numeroCuenta = numeroCuenta?.Trim() ?? "";
            using (var conn = new MySqlConnection(_connectionString))
            {
                conn.Open();
                return ObtenerSaldo(conn, null, numeroCuenta);
            }
        }

        private decimal ObtenerSaldo(MySqlConnection conn, MySqlTransaction transaction, string numeroCuenta)
        {
            var query = "SELECT saldo FROM cuentas WHERE numero_cuenta = @numero";

            using (var cmd = new MySqlCommand(query, conn, transaction))
            {
                cmd.Parameters.AddWithValue("@numero", numeroCuenta);
                var result = cmd.ExecuteScalar();

                if (result == null || result == DBNull.Value)
                    throw new Exception("Cuenta no encontrada");

                return Convert.ToDecimal(result);
            }
        }

        public List<Cuenta> ObtenerTodasLasCuentas()
        {
            var cuentas = new List<Cuenta>();

            using (var conn = new MySqlConnection(_connectionString))
            {
                conn.Open();
                var query = "SELECT * FROM cuentas";

                using (var cmd = new MySqlCommand(query, conn))
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
                            FechaCreacion = reader.GetDateTime("fecha_creacion")
                        });
                    }
                }
            }
            return cuentas;
        }

        public List<Transaccion> ObtenerTodasLasTransacciones()
        {
            var transacciones = new List<Transaccion>();

            using (var conn = new MySqlConnection(_connectionString))
            {
                conn.Open();
                var query = "SELECT * FROM transacciones ORDER BY fecha DESC";

                using (var cmd = new MySqlCommand(query, conn))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        transacciones.Add(new Transaccion
                        {
                            Id = reader.GetInt32("id"),
                            CuentaOrigen = reader.GetString("cuenta_origen"),
                            CuentaDestino = SafeGetString(reader, "cuenta_destino"),
                            Monto = reader.GetDecimal("monto"),
                            SaldoAnteriorOrigen = SafeGetDecimal(reader, "saldo_anterior_origen"),
                            SaldoActualOrigen = SafeGetDecimal(reader, "saldo_actual_origen"),
                            SaldoAnteriorDestino = SafeGetDecimal(reader, "saldo_anterior_destino"),
                            SaldoActualDestino = SafeGetDecimal(reader, "saldo_actual_destino"),
                            Fecha = reader.GetDateTime("fecha"),
                            Estado = reader.GetString("estado")
                        });
                    }
                }
            }
            return transacciones;
        }

        // Métodos auxiliares para manejo seguro de nulos
        private string SafeGetString(MySqlDataReader reader, string colName)
        {
            int colIndex = reader.GetOrdinal(colName);
            return reader.IsDBNull(colIndex) ? null : reader.GetString(colIndex);
        }

        private decimal? SafeGetDecimal(MySqlDataReader reader, string colName)
        {
            int colIndex = reader.GetOrdinal(colName);
            return reader.IsDBNull(colIndex) ? (decimal?)null : reader.GetDecimal(colIndex);
        }
    }
}
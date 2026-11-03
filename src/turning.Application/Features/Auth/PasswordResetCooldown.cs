using System.Collections.Concurrent;

namespace Turning.Application.Features.Auth;

/// <summary>
/// Limita el restablecimiento a un correo por cuenta y minuto.
/// </summary>
/// <remarks>
/// shortcut: vive en la memoria del proceso y sirve con una sola instancia de la API, igual que
/// SessionOperationLock; si la API escala a varias réplicas, moverlo a la base o a un caché compartido.
/// </remarks>
public sealed class PasswordResetCooldown
{
    /// <summary>Tiempo mínimo entre dos correos a la misma cuenta.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(60);

    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastSent = new(StringComparer.Ordinal);

    /// <summary>
    /// Reserva el envío para la cuenta si no hubo otro en el último minuto.
    /// </summary>
    public bool TryReserve(string accountKey, DateTimeOffset now)
    {
        // Poda: solo quedan las cuentas que pidieron un correo en el último minuto.
        foreach (var entry in _lastSent)
            if (now - entry.Value >= Window) _lastSent.TryRemove(entry);

        while (true)
        {
            if (!_lastSent.TryGetValue(accountKey, out var last))
            {
                if (_lastSent.TryAdd(accountKey, now)) return true;
                continue;
            }

            if (now - last < Window) return false;
            if (_lastSent.TryUpdate(accountKey, now, last)) return true;
        }
    }

    /// <summary>
    /// Libera una reserva cuyo envío falló, para que la cuenta pueda reintentar enseguida.
    /// </summary>
    public void Release(string accountKey, DateTimeOffset reservedAt) =>
        _lastSent.TryRemove(new KeyValuePair<string, DateTimeOffset>(accountKey, reservedAt));
}

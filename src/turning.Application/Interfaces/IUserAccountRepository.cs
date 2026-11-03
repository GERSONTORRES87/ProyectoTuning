using Turning.Domain.Entities;

namespace Turning.Application.Interfaces;

/// <summary>
/// Contrato de persistencia para usuarios autenticables.
/// </summary>
public interface IUserAccountRepository
{
    /// <summary>
    /// Busca un usuario por su correo normalizado.
    /// </summary>
    Task<UserAccount?> GetByEmailAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>
    /// Busca un usuario por su identificador.
    /// </summary>
    Task<UserAccount?> GetByIdAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Indica si ya existe un usuario con el correo dado.
    /// </summary>
    Task<bool> ExistsByEmailAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>
    /// Busca cuentas no eliminadas cuyo nombre o correo contiene el texto, ordenadas por correo.
    /// </summary>
    Task<(IReadOnlyList<UserAccount> Items, int Total)> SearchAsync(string? query, int page, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>
    /// Persiste un nuevo usuario.
    /// </summary>
    Task AddAsync(UserAccount userAccount, CancellationToken cancellationToken = default);

    /// <summary>
    /// Guarda cambios pendientes sobre usuarios.
    /// </summary>
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
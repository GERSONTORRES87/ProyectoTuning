namespace Turning.Application.Features.Auth;

/// <summary>
/// Página de cuentas para administración. No incluye contraseñas ni hashes.
/// </summary>
public sealed class PagedUsersResult
{
    /// <summary>Cuentas de la página.</summary>
    public required List<AuthenticatedUser> Items { get; init; }

    /// <summary>Total de cuentas que cumplen el filtro.</summary>
    public int Total { get; init; }

    /// <summary>Página devuelta, desde 1.</summary>
    public int Page { get; init; }

    /// <summary>Tamaño de página.</summary>
    public int PageSize { get; init; }
}

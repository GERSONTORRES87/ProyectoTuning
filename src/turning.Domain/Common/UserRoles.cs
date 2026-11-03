namespace Turning.Domain.Common;

/// <summary>
/// Roles conocidos del sistema.
///
/// El registro público crea Participant; los privilegios los asigna un administrador.
/// </summary>
public static class UserRoles
{
    /// <summary>
    /// Participante del experimento. Solo accede a sus propias sesiones.
    /// </summary>
    public const string Participant = "Participant";

    /// <summary>
    /// Investigador. Accede a sesiones ajenas para análisis.
    /// </summary>
    public const string Researcher = "Researcher";

    /// <summary>
    /// Administrador. Además puede cancelar sesiones de cualquier participante.
    /// </summary>
    public const string Administrator = "Administrator";

    /// <summary>
    /// Todos los roles que el sistema reconoce.
    /// </summary>
    public static readonly IReadOnlyList<string> All =
    [
        Participant,
        Researcher,
        Administrator
    ];

    /// <summary>
    /// Indica si el rol es uno de los reconocidos por el sistema.
    /// </summary>
    public static bool IsKnown(string? role) =>
        role is not null && All.Contains(role.Trim(), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Devuelve el rol con la grafía canónica del sistema.
    /// </summary>
    public static string Normalize(string role) =>
        All.FirstOrDefault(known => known.Equals(role.Trim(), StringComparison.OrdinalIgnoreCase))
        ?? role.Trim();
}

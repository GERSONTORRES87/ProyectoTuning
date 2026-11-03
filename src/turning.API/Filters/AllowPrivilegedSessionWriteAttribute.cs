namespace Turning.API.Filters;

/// <summary>
/// Permite que un Researcher o Administrator ajeno a la sesión llegue a esta ruta con un verbo
/// que escribe (POST, PUT, DELETE...).
/// </summary>
/// <remarks>
/// Es opt-in por la misma razón que <see cref="AllowSessionInterlocutorAttribute"/>: el nivel
/// <c>Privileged</c> es el más alto, así que sin esta marca superaría el mínimo <c>Owner</c> de
/// cualquier ruta y un observador podría, por ejemplo, enviar la encuesta o las emociones de
/// otro participante. Observar (GET/HEAD) no necesita la marca.
///
/// La llevan solo las rutas cuyo servicio comprueba la participación real o que son
/// operaciones de gestión reservadas a esos roles: la conversación (decide dueño / interlocutor
/// asignado / 404) y la asignación y liberación del interlocutor.
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class AllowPrivilegedSessionWriteAttribute : Attribute
{
}

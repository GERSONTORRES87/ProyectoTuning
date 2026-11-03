namespace Turning.API.Middleware;

/// <summary>
/// Cuerpo que devuelve <see cref="ExceptionHandlingMiddleware"/> cuando una
/// excepción llega sin que el controller la haya traducido.
///
/// Incluye un código estable para que el cliente no dependa del texto.
/// </summary>
public sealed class ApiExceptionResponse
{
    public string? Error { get; init; }
    public string? Code { get; init; }
    /// <summary>
    /// Mensaje legible del error.
    /// </summary>
    public string? Message { get; init; }

}

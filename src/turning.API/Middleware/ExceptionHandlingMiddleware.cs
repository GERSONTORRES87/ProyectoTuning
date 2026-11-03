namespace Turning.API.Middleware;

using Turning.Application.Exceptions;
using TurningApplicationException = Turning.Application.Exceptions.ApplicationException;

/// <summary>
/// Middleware para manejo global de excepciones.
/// </summary>
public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    /// <summary>
    /// Constructor del middleware de excepción.
    /// </summary>
    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    /// <summary>
    /// Invoca el middleware.
    /// </summary>
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Excepción no manejada en la solicitud");
            await HandleExceptionAsync(context, ex);
        }
    }

    /// <summary>
    /// Maneja la excepción y genera una respuesta HTTP apropiada.
    /// </summary>
    private static Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.ContentType = "application/json";

        var statusCode = StatusCodes.Status500InternalServerError;
        var message = "Ocurrió un error interno en el servidor";
        var code = "INTERNAL_ERROR";

        if (exception is NotFoundException)
        {
            statusCode = StatusCodes.Status404NotFound;
            message = exception.Message;
            code = "RESOURCE_NOT_FOUND";
        }
        else if (exception is TurningApplicationException applicationException)
        {
            statusCode = applicationException.Code switch
            {
                "AUTH_INVALID_CREDENTIALS" => StatusCodes.Status401Unauthorized,
                "AUTH_USER_ALREADY_EXISTS" => StatusCodes.Status409Conflict,
                "AUTH_FORBIDDEN" or "AUTH_ROLE_FORBIDDEN" => StatusCodes.Status403Forbidden,
                "SESSION_NOT_FOUND" => StatusCodes.Status404NotFound,
                "SESSION_CONFLICT" or "SESSION_TERMINAL" or "SESSION_NOT_STARTED" => StatusCodes.Status409Conflict,
                _ => StatusCodes.Status400BadRequest
            };

            message = exception.Message;
            code = applicationException.Code ?? "INVALID_REQUEST";
        }

        var response = new
        {
            error = code,
            code,
            message
        };

        context.Response.StatusCode = statusCode;

        return context.Response.WriteAsJsonAsync(response);
    }
}

/// <summary>
/// Extensiones para agregar el middleware de excepción.
/// </summary>
public static class ExceptionHandlingMiddlewareExtensions
{
    /// <summary>
    /// Usa el middleware de manejo de excepciones.
    /// </summary>
    public static IApplicationBuilder UseExceptionHandling(this IApplicationBuilder app)
    {
        return app.UseMiddleware<ExceptionHandlingMiddleware>();
    }
}

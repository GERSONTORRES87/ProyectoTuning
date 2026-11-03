using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Turning.API.Filters;
using Turning.Application.Features.ExperimentSessions;
using Xunit;

namespace Turning.API.Tests;

/// <summary>
/// Pruebas del filtro de aislamiento por propietario.
/// </summary>
/// <remarks>
/// Este filtro es la única barrera entre los datos de un participante y los de otro, y se
/// aplica globalmente a toda ruta que lleve <c>{sessionId}</c>.
///
/// Desde que una sesión puede tener dos personas, el filtro resuelve un
/// <see cref="SessionAccessLevel"/> y exige <c>Owner</c> salvo que la ruta declare
/// <see cref="AllowSessionInterlocutorAttribute"/>. Esa distinción es lo más delicado del
/// cambio: bajar el listón a "dueño o interlocutor" en todas las rutas le habría dado al
/// interlocutor permiso para cancelar la sesión y leer los resultados del participante, así
/// que aquí se comprueba en ambos sentidos.
/// </remarks>
public class SessionOwnershipFilterTests
{
    private readonly IExperimentSessionService _sessions = Substitute.For<IExperimentSessionService>();

    // DefaultHttpContext arranca con el verbo vacio, que el filtro trataria como una escritura.
    private string _method = "GET";

    private static readonly Guid SessionId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid UserId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    /// <summary>
    /// Construye el contexto del filtro con la ruta, la identidad y los metadatos indicados.
    /// </summary>
    private AuthorizationFilterContext CreateContext(
        object? routeSessionId,
        ClaimsPrincipal? user,
        params object[] endpointMetadata)
    {
        var services = new ServiceCollection();
        services.AddSingleton(_sessions);

        var httpContext = new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider(),
            User = user ?? new ClaimsPrincipal(new ClaimsIdentity())
        };
        httpContext.Request.Method = _method;

        var routeData = new RouteData();
        if (routeSessionId is not null)
            routeData.Values[SessionOwnershipFilter.RouteParameterName] = routeSessionId;

        var actionDescriptor = new ActionDescriptor
        {
            EndpointMetadata = endpointMetadata.ToList()
        };

        var actionContext = new ActionContext(httpContext, routeData, actionDescriptor);
        return new AuthorizationFilterContext(actionContext, []);
    }

    private static ClaimsPrincipal Authenticated(Guid? userId, params string[] roles)
    {
        var claims = new List<Claim>();

        if (userId is not null)
            claims.Add(new Claim(ClaimTypes.NameIdentifier, userId.Value.ToString()));

        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));

        // El esquema de autenticación no vacío es lo que hace IsAuthenticated == true.
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth", ClaimTypes.Name, ClaimTypes.Role));
    }

    private void ArrangeLevel(SessionAccessLevel level, bool isPrivileged = false) =>
        _sessions.GetAccessLevelAsync(SessionId, UserId, isPrivileged, Arg.Any<CancellationToken>()).Returns(level);

    private static void ShouldBeNotFoundWithSessionCode(IActionResult? result)
    {
        // 404 y no 403: quien pregunta no debe poder distinguir una sesión ajena de una
        // inexistente enumerando GUIDs.
        result.Should().BeOfType<NotFoundObjectResult>();
        var payload = ((NotFoundObjectResult)result!).Value!;
        payload.GetType().GetProperty("error")!.GetValue(payload).Should().Be("SESSION_NOT_FOUND");
    }

    [Fact]
    public async Task ShouldReturnNotFound_WhenTheCallerHasNothingToDoWithTheSession()
    {
        ArrangeLevel(SessionAccessLevel.None);
        var context = CreateContext(SessionId.ToString(), Authenticated(UserId));

        await new SessionOwnershipFilter().OnAuthorizationAsync(context);

        ShouldBeNotFoundWithSessionCode(context.Result);
    }

    [Fact]
    public async Task ShouldLetTheOwnerThrough()
    {
        ArrangeLevel(SessionAccessLevel.Owner);
        var context = CreateContext(SessionId.ToString(), Authenticated(UserId));

        await new SessionOwnershipFilter().OnAuthorizationAsync(context);

        context.Result.Should().BeNull();
    }

    [Fact]
    public async Task ShouldRejectTheInterlocutor_OnRoutesWithoutTheOptIn()
    {
        ArrangeLevel(SessionAccessLevel.Interlocutor);
        var context = CreateContext(SessionId.ToString(), Authenticated(UserId));

        await new SessionOwnershipFilter().OnAuthorizationAsync(context);

        // Lo seguro es lo que pasa por omisión: sin el atributo, la ruta exige ser el dueño.
        // Es lo que impide que el interlocutor cancele la sesión o lea sus resultados.
        ShouldBeNotFoundWithSessionCode(context.Result);
    }

    [Fact]
    public async Task ShouldAcceptTheInterlocutor_OnRoutesWithTheOptIn()
    {
        ArrangeLevel(SessionAccessLevel.Interlocutor);
        var context = CreateContext(
            SessionId.ToString(), Authenticated(UserId), new AllowSessionInterlocutorAttribute());

        await new SessionOwnershipFilter().OnAuthorizationAsync(context);

        context.Result.Should().BeNull();
    }

    [Fact]
    public async Task ShouldStillRejectAStranger_EvenOnRoutesWithTheOptIn()
    {
        ArrangeLevel(SessionAccessLevel.None);
        var context = CreateContext(
            SessionId.ToString(), Authenticated(UserId), new AllowSessionInterlocutorAttribute());

        await new SessionOwnershipFilter().OnAuthorizationAsync(context);

        // El opt-in abre la puerta al interlocutor asignado, no a cualquiera.
        ShouldBeNotFoundWithSessionCode(context.Result);
    }

    [Fact]
    public async Task ShouldExposeTheResolvedLevel_ForTheController()
    {
        ArrangeLevel(SessionAccessLevel.Interlocutor);
        var context = CreateContext(
            SessionId.ToString(), Authenticated(UserId), new AllowSessionInterlocutorAttribute());

        await new SessionOwnershipFilter().OnAuthorizationAsync(context);

        // El controller lo usa para saber con qué papel entra quien llama, sin repetir la
        // consulta.
        context.HttpContext.Items[SessionOwnershipFilter.SessionAccessLevelKey]
            .Should().Be(SessionAccessLevel.Interlocutor);
    }

    [Theory]
    [InlineData("Researcher")]
    [InlineData("Administrator")]
    public async Task ShouldMarkPrivilegedRoles_SoTheServiceCanDecide(string role)
    {
        ArrangeLevel(SessionAccessLevel.Privileged, isPrivileged: true);
        var context = CreateContext(SessionId.ToString(), Authenticated(UserId, role));

        await new SessionOwnershipFilter().OnAuthorizationAsync(context);

        context.Result.Should().BeNull();
        await _sessions.Received(1).GetAccessLevelAsync(
            SessionId, UserId, true, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task ShouldKeepPrivilegedObserversFromWriting_OnRoutesWithoutTheOptIn(string method)
    {
        _method = method;
        ArrangeLevel(SessionAccessLevel.Privileged, isPrivileged: true);
        var context = CreateContext(SessionId.ToString(), Authenticated(UserId, "Researcher"));

        await new SessionOwnershipFilter().OnAuthorizationAsync(context);

        // Sin esto, un investigador ajeno podia enviar la encuesta o las emociones del dueno.
        ShouldBeNotFoundWithSessionCode(context.Result);
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("HEAD")]
    public async Task ShouldLetPrivilegedObserversRead(string method)
    {
        _method = method;
        ArrangeLevel(SessionAccessLevel.Privileged, isPrivileged: true);
        var context = CreateContext(SessionId.ToString(), Authenticated(UserId, "Administrator"));

        await new SessionOwnershipFilter().OnAuthorizationAsync(context);

        context.Result.Should().BeNull();
    }

    [Theory]
    [InlineData("Researcher")]
    [InlineData("Administrator")]
    public async Task ShouldLetPrivilegedRolesWrite_WhereTheRouteOptsIn(string role)
    {
        _method = "POST";
        ArrangeLevel(SessionAccessLevel.Privileged, isPrivileged: true);
        var context = CreateContext(
            SessionId.ToString(), Authenticated(UserId, role), new AllowPrivilegedSessionWriteAttribute());

        await new SessionOwnershipFilter().OnAuthorizationAsync(context);

        context.Result.Should().BeNull();
    }

    [Fact]
    public async Task ShouldStillLetAPrivilegedOwnerWrite_WithoutTheOptIn()
    {
        // El dueno que ademas es investigador resuelve como Owner, no como Privileged.
        _method = "POST";
        ArrangeLevel(SessionAccessLevel.Owner, isPrivileged: true);
        var context = CreateContext(SessionId.ToString(), Authenticated(UserId, "Researcher"));

        await new SessionOwnershipFilter().OnAuthorizationAsync(context);

        context.Result.Should().BeNull();
    }

    [Fact]
    public async Task ShouldLetTheAssignedInterlocutorWrite_OnRoutesWithTheInterlocutorOptIn()
    {
        _method = "POST";
        ArrangeLevel(SessionAccessLevel.Interlocutor);
        var context = CreateContext(
            SessionId.ToString(), Authenticated(UserId), new AllowSessionInterlocutorAttribute());

        await new SessionOwnershipFilter().OnAuthorizationAsync(context);

        context.Result.Should().BeNull();
    }

    [Fact]
    public async Task ShouldNotTreatOtherRolesAsPrivileged()
    {
        ArrangeLevel(SessionAccessLevel.None);
        var context = CreateContext(SessionId.ToString(), Authenticated(UserId, "Participant"));

        await new SessionOwnershipFilter().OnAuthorizationAsync(context);

        ShouldBeNotFoundWithSessionCode(context.Result);
        await _sessions.Received(1).GetAccessLevelAsync(
            SessionId, UserId, false, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ShouldReturnUnauthorized_WhenTheCallerIsNotAuthenticated()
    {
        var context = CreateContext(SessionId.ToString(), user: null);

        await new SessionOwnershipFilter().OnAuthorizationAsync(context);

        // Cubre el caso de un endpoint anónimo que llevara {sessionId}: sin identidad no hay
        // nada que comparar, así que no puede pasar de largo.
        context.Result.Should().BeOfType<UnauthorizedResult>();
        await _sessions.DidNotReceive().GetAccessLevelAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ShouldReturnUnauthorized_WhenTheTokenHasNoUsableUserId()
    {
        var context = CreateContext(SessionId.ToString(), Authenticated(userId: null));

        await new SessionOwnershipFilter().OnAuthorizationAsync(context);

        // Un token autenticado pero sin identificador utilizable no puede resolverse a un
        // propietario: dejarlo pasar equivaldría a desactivar el aislamiento.
        context.Result.Should().BeOfType<UnauthorizedResult>();
    }

    [Fact]
    public async Task ShouldIgnoreRoutesWithoutSessionId()
    {
        var context = CreateContext(routeSessionId: null, Authenticated(UserId));

        await new SessionOwnershipFilter().OnAuthorizationAsync(context);

        // Rutas como GET /api/results o POST /api/sessions/join no son asunto de este filtro.
        context.Result.Should().BeNull();
        await _sessions.DidNotReceive().GetAccessLevelAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ShouldIgnoreRoutesWhoseSessionIdIsNotAGuid()
    {
        var context = CreateContext("no-es-un-guid", Authenticated(UserId));

        await new SessionOwnershipFilter().OnAuthorizationAsync(context);

        // El enrutamiento ya habrá rechazado la petición por la restricción :guid; aquí solo
        // se comprueba que el filtro no invente una sesión ni reviente.
        context.Result.Should().BeNull();
        await _sessions.DidNotReceive().GetAccessLevelAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ShouldRespectTheSkipAttribute()
    {
        var context = CreateContext(SessionId.ToString(), Authenticated(UserId), new SkipSessionOwnershipAttribute());

        await new SessionOwnershipFilter().OnAuthorizationAsync(context);

        context.Result.Should().BeNull();
        await _sessions.DidNotReceive().GetAccessLevelAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ShouldNotOverrideAnEarlierResult()
    {
        var context = CreateContext(SessionId.ToString(), Authenticated(UserId));
        context.Result = new BadRequestResult();

        await new SessionOwnershipFilter().OnAuthorizationAsync(context);

        // Si otro filtro ya decidió, este no lo pisa ni gasta una consulta.
        context.Result.Should().BeOfType<BadRequestResult>();
        await _sessions.DidNotReceive().GetAccessLevelAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ShouldAcceptTheSubClaim_AsIssuedByTheTokenService()
    {
        // El token emitido por la API trae el identificador en "sub"; ASP.NET a veces lo
        // mapea a NameIdentifier y a veces no, y el filtro tiene que resolver ambos.
        var identity = new ClaimsIdentity(
            [new Claim(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub, UserId.ToString())],
            "TestAuth");

        ArrangeLevel(SessionAccessLevel.Owner);
        var context = CreateContext(SessionId.ToString(), new ClaimsPrincipal(identity));

        await new SessionOwnershipFilter().OnAuthorizationAsync(context);

        context.Result.Should().BeNull();
    }

    [Fact]
    public async Task ShouldAcceptAGuidRouteValue_NotOnlyItsStringForm()
    {
        ArrangeLevel(SessionAccessLevel.Owner);
        var context = CreateContext(SessionId, Authenticated(UserId));

        await new SessionOwnershipFilter().OnAuthorizationAsync(context);

        context.Result.Should().BeNull();
    }
}

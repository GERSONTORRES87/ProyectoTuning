using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Turning.Application.Features.Auth;
using Turning.Application.Interfaces;
using Turning.Domain.Entities;

namespace Turning.Infrastructure.Security;

/// <summary>
/// Emite tokens JWT para usuarios autenticados.
/// </summary>
public sealed class JwtTokenService : ITokenService
{
    private readonly JwtOptions _options;

    /// <summary>
    /// Constructor del servicio JWT.
    /// </summary>
    public JwtTokenService(IOptions<JwtOptions> options)
    {
        _options = options.Value;
    }

    /// <inheritdoc />
    public AuthResult CreateToken(UserAccount userAccount)
    {
        var errors = _options.Validate();
        if (errors.Count > 0)
            throw new InvalidOperationException(
                "No se puede emitir el token: configuracion Jwt invalida. " + string.Join(" | ", errors));

        var issuer = _options.Issuer;
        var audience = _options.Audience;
        var signingKey = _options.SigningKey!;
        var expirationMinutes = _options.ExpirationMinutes;

        var expiresAtUtc = DateTime.UtcNow.AddMinutes(expirationMinutes);
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
            SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userAccount.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, userAccount.Email),
            new(JwtRegisteredClaimNames.UniqueName, userAccount.FullName),
            new(ClaimTypes.NameIdentifier, userAccount.Id.ToString()),
            new(ClaimTypes.Name, userAccount.FullName),
            new(ClaimTypes.Email, userAccount.Email),
            new(ClaimTypes.Role, userAccount.Role),
            new(PasswordStamp.ClaimType, PasswordStamp.Of(userAccount))
        };

        var tokenDescriptor = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: expiresAtUtc,
            signingCredentials: credentials);

        var accessToken = new JwtSecurityTokenHandler().WriteToken(tokenDescriptor);

        return new AuthResult
        {
            AccessToken = accessToken,
            ExpiresAtUtc = expiresAtUtc,
            User = new AuthenticatedUser
            {
                Id = userAccount.Id,
                FullName = userAccount.FullName,
                Email = userAccount.Email,
                Role = userAccount.Role
            }
        };
    }
}
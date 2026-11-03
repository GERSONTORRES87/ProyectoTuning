namespace turning.Web.Auth;

public sealed class LoginRequestDto
{
    public required string Email { get; init; }

    public required string Password { get; init; }
}

public sealed class RegisterRequestDto
{
    public required string FullName { get; init; }

    public required string Email { get; init; }

    public required string Password { get; init; }

    public string Role { get; init; } = "Participant";
}

public sealed class AuthResultDto
{
    public required string AccessToken { get; init; }

    public required DateTime ExpiresAtUtc { get; init; }

    public required AuthenticatedUserDto User { get; init; }
}

public sealed class AuthenticatedUserDto
{
    public Guid Id { get; init; }

    public required string FullName { get; init; }

    public required string Email { get; init; }

    public required string Role { get; init; }
}

public sealed class PagedUsersDto
{
    public List<AuthenticatedUserDto> Items { get; init; } = [];

    public int Total { get; init; }

    public int Page { get; init; }

    public int PageSize { get; init; }
}

public sealed class ApiErrorResponse
{
    public string? Message { get; init; }

    public string? Details { get; init; }

    public string? Type { get; init; }
}

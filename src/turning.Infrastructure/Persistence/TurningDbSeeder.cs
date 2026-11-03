using Microsoft.EntityFrameworkCore;
using Turning.Application.Interfaces;
using Turning.Domain.Entities;

namespace Turning.Infrastructure.Persistence;

/// <summary>Inicializa la encuesta y, sólo en Development, cuentas locales utilizables.</summary>
public static class TurningDbSeeder
{
    public const string DemoPassword = "TurningDemo2026!";

    public static async Task SeedAsync(
        TurningDbContext db,
        IPasswordHasherService passwordHasher,
        bool includeDemoAccounts,
        CancellationToken ct = default)
    {
        await SurveyDefinitionSeeder.EnsureDefaultAsync(db, ct);
        if (!includeDemoAccounts) return;

        await EnsureDemoUserAsync(db, passwordHasher, "admin@turning.local", "Administrador Demo", "Administrator", ct);
        await EnsureDemoUserAsync(db, passwordHasher, "researcher@turning.local", "Investigador Demo", "Researcher", ct);
        await db.SaveChangesAsync(ct);
    }

    private static async Task EnsureDemoUserAsync(
        TurningDbContext db, IPasswordHasherService passwordHasher,
        string email, string name, string role, CancellationToken ct)
    {
        var normalizedEmail = UserAccount.NormalizeEmail(email);
        var user = await db.UserAccounts.SingleOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail, ct);
        if (user is null)
        {
            user = UserAccount.Create(email, name, "pending", role);
            user.UpdatePasswordHash(passwordHasher.HashPassword(user, DemoPassword));
            db.UserAccounts.Add(user);
        }
        else if (user.PasswordHash.StartsWith("$2a$11$seed.hash.placeholder.", StringComparison.Ordinal))
        {
            // Repara únicamente los hashes ficticios del seed antiguo; conserva cuentas reales.
            user.UpdatePasswordHash(passwordHasher.HashPassword(user, DemoPassword));
        }
    }
}

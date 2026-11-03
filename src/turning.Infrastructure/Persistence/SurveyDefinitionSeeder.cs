using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Turning.Domain.Entities;

namespace Turning.Infrastructure.Persistence;

public static class SurveyDefinitionSeeder
{
    public static async Task<SurveyDefinition> EnsureDefaultAsync(TurningDbContext db, CancellationToken ct = default)
    {
        var definition = await db.SurveyDefinitions.Include(d => d.Questions)
            .FirstOrDefaultAsync(d => d.IsActive, ct);
        if (definition is not null && (definition.Code != "default-v1" || definition.Questions.Count > 0))
            return definition;

        definition ??= await db.SurveyDefinitions.Include(d => d.Questions)
            .FirstOrDefaultAsync(d => d.Code == "default-v1", ct);
        if (definition is null)
        {
            definition = SurveyDefinition.Create("default-v1", "Encuesta posterior a la conversación");
            db.SurveyDefinitions.Add(definition);
        }
        definition.Activate();
        if (definition.Questions.Count == 0)
        {
            definition.AddQuestion("perceived_interlocutor", "¿Con quién crees que conversaste?", "choice", true, 1);
            definition.AddQuestion("empathy", "Sentí empatía durante la conversación (1: nada, 5: mucha).", "scale", true, 2);
            definition.AddQuestion("understanding", "Me sentí comprendido/a (1: nada, 5: mucho).", "scale", true, 3);
            definition.AddQuestion("comments", "¿Quieres añadir algún comentario?", "text", false, 4);
            db.Set<SurveyQuestion>().AddRange(definition.Questions);
        }
        try
        {
            await db.SaveChangesAsync(ct);
            return definition;
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            // Dos peticiones pueden inicializar la misma definición a la vez.
            foreach (var question in definition.Questions) db.Entry(question).State = EntityState.Detached;
            db.Entry(definition).State = EntityState.Detached;
            return await db.SurveyDefinitions.Include(d => d.Questions).FirstAsync(d => d.IsActive, ct);
        }
    }
}

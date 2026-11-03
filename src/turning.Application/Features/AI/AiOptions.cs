namespace Turning.Application.Features.AI;

/// <summary>
/// Configuración de un proveedor compatible con la API de chat de OpenAI.
/// </summary>
/// <remarks>
/// La misma clase sirve para OpenAI y para OpenRouter: comparten el endpoint
/// <c>/chat/completions</c> y el formato de mensajes, y lo que cambia (URL base, modelo,
/// clave y un par de cabeceras) es exactamente lo que hay aquí.
/// </remarks>
public sealed class AiProviderOptions
{
    /// <summary>
    /// Nombre con el que este proveedor aparece en el contrato y en los eventos.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Permite apagar el proveedor sin borrar su configuración.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// URL base de la API, por ejemplo <c>https://api.openai.com/v1/</c>.
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Modelo a usar.
    /// </summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>
    /// Clave de API.
    /// </summary>
    /// <remarks>
    /// En <c>appsettings.json</c> está declarada con un valor de marcador de posición, para
    /// que se vea qué variable hace falta y con qué nombre. <b>La clave real nunca se
    /// versiona</b>: se sobreescribe con user-secrets o variable de entorno, igual que la
    /// clave de firma del JWT. Ver <see cref="PlaceholderApiKeys"/>.
    /// </remarks>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Tiempo máximo de un intento individual, en milisegundos.
    /// </summary>
    /// <remarks>
    /// Es deliberadamente menor que el presupuesto total: tres intentos más el salto al
    /// siguiente proveedor tienen que caber dentro de <see cref="AiOptions.TotalBudgetMs"/>.
    /// </remarks>
    public int RequestTimeoutMs { get; set; } = 4000;

    /// <summary>
    /// Límite de tokens de la respuesta.
    /// </summary>
    public int MaxOutputTokens { get; set; } = 300;

    /// <summary>
    /// Temperatura de muestreo.
    /// </summary>
    public double Temperature { get; set; } = 0.7;

    /// <summary>
    /// Envía <c>reasoning: {enabled: false}</c> para apagar el razonamiento del modelo.
    /// </summary>
    /// <remarks>
    /// Solo aplica a OpenRouter. Sus modelos gratuitos de razonamiento gastan
    /// <see cref="MaxOutputTokens"/> pensando y devuelven <c>content</c> nulo. OpenAI no
    /// conoce el parámetro, por eso es opcional y viene apagado.
    /// </remarks>
    public bool DisableReasoning { get; set; }

    /// <summary>
    /// Cabecera <c>HTTP-Referer</c> que OpenRouter usa para atribuir el tráfico. Opcional.
    /// </summary>
    public string? HttpReferer { get; set; }

    /// <summary>
    /// Cabecera <c>X-Title</c> que OpenRouter muestra en su panel. Opcional.
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// Valores que se reconocen como marcador de posición y no como una clave real.
    /// </summary>
    /// <remarks>
    /// La configuración declara las claves aunque todavía no existan, para que se vea dónde
    /// van y con qué nombre. Sin esta lista, un marcador de posición se tomaría por válido:
    /// el proveedor entraría en la cadena, saldría a la red, recibiría un 401 y recién ahí
    /// se saltaría — una llamada inútil y su latencia en el primer turno de cada sesión.
    ///
    /// Reconocerlos permite dejar las variables declaradas y que el sistema se comporte
    /// exactamente igual que si no estuvieran.
    /// </remarks>
    public static readonly IReadOnlyList<string> PlaceholderApiKeys =
    [
        "dummy",
        "changeme",
        "cambiar",
        "reemplazar",
        "pendiente",
        "todo",
        "tbd",
        "none",
        "null",
        "your-api-key",
        "your-api-key-here",
        "sk-xxx",
        "sk-...",
        "sk-or-xxx"
    ];

    /// <summary>
    /// Indica si el valor configurado es un marcador de posición en vez de una clave real.
    /// </summary>
    public static bool IsPlaceholderApiKey(string? apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            return true;

        var normalized = apiKey.Trim();

        return PlaceholderApiKeys.Any(placeholder =>
                   normalized.Equals(placeholder, StringComparison.OrdinalIgnoreCase))
               // Cubre variantes como "dummy-openai-key" o "DUMMY_KEY_OPENROUTER".
               || normalized.StartsWith("dummy", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("reemplazar", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("placeholder", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Indica si la clave configurada es un marcador de posición.
    /// </summary>
    public bool HasPlaceholderKey => IsPlaceholderApiKey(ApiKey);

    /// <summary>
    /// Indica si el proveedor tiene lo mínimo para intentar una llamada.
    /// </summary>
    /// <remarks>
    /// Un proveedor sin clave —o con un marcador de posición— no es un error de
    /// configuración: significa que ese eslabón de la cadena no está disponible y se salta.
    /// Por eso esto no forma parte de <see cref="AiOptions.Validate"/>.
    /// </remarks>
    public bool IsUsable =>
        Enabled
        && !HasPlaceholderKey
        && !string.IsNullOrWhiteSpace(BaseUrl)
        && !string.IsNullOrWhiteSpace(Model);
}

/// <summary>
/// Configuración de la generación de texto mediada por IA.
/// </summary>
public sealed class AiOptions
{
    /// <summary>
    /// Sección de configuración que contiene estas opciones.
    /// </summary>
    public const string SectionName = "Ai";

    /// <summary>
    /// Nombre reservado del adaptador por reglas, que cierra siempre la cadena.
    /// </summary>
    public const string RuleBasedProviderName = "rule-based";

    /// <summary>
    /// Orden en que se intentan los proveedores. El primero que responde gana.
    /// </summary>
    public IList<string> ProviderOrder { get; set; } = ["openai", "openrouter", RuleBasedProviderName];

    /// <summary>
    /// Configuración de OpenAI.
    /// </summary>
    public AiProviderOptions OpenAi { get; set; } = new()
    {
        Name = "openai",
        BaseUrl = "https://api.openai.com/v1/",
        Model = "gpt-4o-mini"
    };

    /// <summary>
    /// Configuración de OpenRouter, el eslabón gratuito de la cadena.
    /// </summary>
    public AiProviderOptions OpenRouter { get; set; } = new()
    {
        Name = "openrouter",
        BaseUrl = "https://openrouter.ai/api/v1/",
        Model = "meta-llama/llama-3.3-70b-instruct:free"
    };

    /// <summary>
    /// Presupuesto total de la cadena, en milisegundos (RF-EXP-01 pide 10 s).
    /// </summary>
    /// <remarks>
    /// RF-EXP-01 habla de 10 s y RF-EXP-07 de 30 s: se concilian entendiendo que 10 s es el
    /// tope de toda la conversación con los proveedores, y el de RF-EXP-07 el techo absoluto
    /// de una petición suelta, que en la práctica nunca se alcanza.
    /// </remarks>
    public int TotalBudgetMs { get; set; } = 10000;

    /// <summary>
    /// Reintentos ante fallos transitorios, por proveedor (RF-EXP-01).
    /// </summary>
    public int MaxRetriesPerProvider { get; set; } = 3;

    /// <summary>
    /// Fallos consecutivos que abren el circuito de un proveedor (RF-EXP-07).
    /// </summary>
    public int CircuitFailureThreshold { get; set; } = 5;

    /// <summary>
    /// Ventana en la que se cuentan esos fallos, en segundos (RF-EXP-07).
    /// </summary>
    public int CircuitWindowSeconds { get; set; } = 60;

    /// <summary>
    /// Tiempo que el circuito permanece abierto antes de volver a probar (RF-EXP-07).
    /// </summary>
    public int CircuitBreakSeconds { get; set; } = 30;

    /// <summary>
    /// Instrucción de sistema que encabeza cada conversación.
    /// </summary>
    public string SystemPrompt { get; set; } =
        "Eres un interlocutor en un experimento de conversación. Responde en español neutro, " +
        "de forma breve, natural y empática. No menciones que eres una inteligencia artificial.";

    /// <summary>
    /// Comprueba la coherencia de la configuración.
    /// </summary>
    /// <remarks>
    /// A diferencia de <c>JwtOptions</c>, la ausencia de claves <b>no</b> es un error: la
    /// cadena está pensada para degradarse hasta el adaptador por reglas, así que faltar una
    /// clave solo significa que ese proveedor se salta. Aquí solo se valida lo que, de estar
    /// mal, rompería la cadena entera.
    /// </remarks>
    /// <returns>Lista de errores; vacía si la configuración es utilizable.</returns>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (ProviderOrder.Count == 0)
            errors.Add($"'{SectionName}:ProviderOrder' no puede estar vacío.");

        // Ultimo, no solo presente: el router sigue este orden y rule-based nunca falla, asi que
        // si va antes, los proveedores externos configurados despues jamas se llamarian.
        if (ProviderOrder.Count == 0
            || !string.Equals(ProviderOrder[^1], RuleBasedProviderName, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add(
                $"'{SectionName}:ProviderOrder' debe terminar en '{RuleBasedProviderName}': es el único " +
                "eslabón que no puede fallar, y sin él una caída de los proveedores externos dejaría " +
                "la conversación sin respuesta.");
        }

        // Una entrada anterior (p. ej. [rule-based, openai, rule-based]) tambien se salta a los externos.
        if (ProviderOrder.Count(p => string.Equals(p, RuleBasedProviderName, StringComparison.OrdinalIgnoreCase)) > 1)
        {
            errors.Add(
                $"'{SectionName}:ProviderOrder' solo puede incluir '{RuleBasedProviderName}' una vez, " +
                "al final: una entrada anterior respondería siempre y los proveedores externos no se llamarían.");
        }

        if (TotalBudgetMs <= 0)
            errors.Add($"'{SectionName}:TotalBudgetMs' debe ser mayor que cero.");

        if (MaxRetriesPerProvider < 0)
            errors.Add($"'{SectionName}:MaxRetriesPerProvider' no puede ser negativo.");

        foreach (var provider in new[] { OpenAi, OpenRouter })
        {
            if (!provider.Enabled)
                continue;

            if (!string.IsNullOrWhiteSpace(provider.BaseUrl)
                && !Uri.TryCreate(provider.BaseUrl, UriKind.Absolute, out _))
            {
                errors.Add($"'{SectionName}:{provider.Name}:BaseUrl' no es una URL absoluta válida.");
            }

            if (provider.RequestTimeoutMs <= 0)
                errors.Add($"'{SectionName}:{provider.Name}:RequestTimeoutMs' debe ser mayor que cero.");

            if (provider.RequestTimeoutMs > TotalBudgetMs)
            {
                errors.Add(
                    $"'{SectionName}:{provider.Name}:RequestTimeoutMs' ({provider.RequestTimeoutMs} ms) supera el " +
                    $"presupuesto total ({TotalBudgetMs} ms): un solo intento agotaría la cadena entera.");
            }
        }

        return errors;
    }
}

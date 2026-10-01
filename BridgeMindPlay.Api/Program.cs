using System.Text.Json;
using BridgeMindPlay.Api;
using Trickster.Bots;
using Trickster.cloud;

public class Program
{
    public static void Main(string[] args)
    {
        BuildApp(args).Run();
    }

    //  bindUrlOverride exists so tests can host on an ephemeral loopback port; production binds 0.0.0.0
    public static WebApplication BuildApp(string[]? args = null, string? bindUrlOverride = null)
    {
        var builder = WebApplication.CreateBuilder(args ?? Array.Empty<string>());
        builder.WebHost.UseUrls(bindUrlOverride ?? ResolveBindUrl());

        var app = builder.Build();

        app.MapGet("/health", () => Results.Json(new { status = "ok" }));
        app.MapPost("/suggest-card", HandleSuggestCard);

        return app;
    }

    private static string ResolveBindUrl()
    {
        var portVar = Environment.GetEnvironmentVariable("PORT");
        if (!int.TryParse(portVar, out var port) || port is < 1 or > 65535)
            port = 8080;

        return $"http://0.0.0.0:{port}";
    }

    private static readonly JsonSerializerOptions JsonBindOptions = new() { PropertyNameCaseInsensitive = true };

    private static async Task<IResult> HandleSuggestCard(HttpContext ctx, ILogger<Program> logger)
    {
        try
        {
            var contentType = ctx.Request.ContentType;
            if (contentType is null || !contentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase))
                return BadRequest("Content-Type must be application/json.");

            using var reader = new StreamReader(ctx.Request.Body);
            var body = await reader.ReadToEndAsync();
            if (string.IsNullOrWhiteSpace(body))
                return BadRequest("Request body is missing.");

            SuggestCardRequest? request;
            try
            {
                request = JsonSerializer.Deserialize<SuggestCardRequest>(body, JsonBindOptions);
            }
            catch (JsonException)
            {
                return BadRequest("Malformed JSON body.");
            }

            if (request is null)
                return BadRequest("Request body is missing.");

            var built = RequestBuilder.Build(request);
            if (built.Error is not null)
                return BadRequest(built.Error);

            var state = built.State!;

            //  production fast path (mirrors Trickster Suggester): one legal card is played without invoking the bot
            if (state.legalCards.Count == 1)
                return SuggestedCard(state.legalCards[0]);

            state.SortCardMembers();

            Card card;
            try
            {
                var bot = new BridgeBot(state.options, state.trumpSuit);
                card = bot.SuggestNextCard(state);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "BridgeBot failed while suggesting a card");
                return InternalError();
            }

            if (card is null || !state.legalCards.Any(c => c.suit == card.suit && c.rank == card.rank))
            {
                logger.LogError("BridgeBot returned card {Suggested} which is not among legalCards (engine failure)", card?.ToString() ?? "null");
                return InternalError();
            }

            return SuggestedCard(card);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled error while processing /suggest-card");
            return InternalError();
        }
    }

    private static IResult SuggestedCard(Card card) => Results.Json(new { suit = (int)card.suit, rank = (int)card.rank });

    private static IResult BadRequest(string error) => Results.Json(new { error }, statusCode: StatusCodes.Status400BadRequest);

    private static IResult InternalError() => Results.Json(new { error = "Internal server error." }, statusCode: StatusCodes.Status500InternalServerError);
}

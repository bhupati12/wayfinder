using System.Text.Json;
using System.Text.Json.Serialization;
using DomainModels.Guides.Models;
using Microsoft.AspNetCore.Mvc;
using SearchService.Application.Services.Interfaces;

namespace SearchService.Controllers;

/// <summary>Grounded answers with citations, as JSON or as a server-sent event stream.</summary>
[ApiController]
[Route("api/ask")]
public sealed class AskController : ControllerBase
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly IAnswerService answers;

    public AskController(IAnswerService answers)
    {
        this.answers = answers;
    }

    /// <summary>Answers a question from the guides.</summary>
    /// <response code="200">The answer, its citations, token counts and timings.</response>
    /// <response code="400">Invalid question or k (problem details).</response>
    /// <response code="503">The chat model is unavailable (problem details).</response>
    [HttpPost]
    [ProducesResponseType<AskResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public Task<AskResponse> Ask([FromBody] AskRequest request, CancellationToken cancellationToken) =>
        answers.AskAsync(request.Question!, request.K, cancellationToken);

    /// <summary>
    /// Streams the answer as server-sent events: "sources" (the numbered sources), then "token"
    /// events with text as it is generated, then "done" with the full response. A failure after
    /// the stream has started is sent as an "error" event.
    /// </summary>
    [HttpPost("stream")]
    [Produces("text/event-stream")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task Stream([FromBody] AskRequest request, CancellationToken cancellationToken)
    {
        await using var events = answers.StreamAsync(request.Question!, request.K, cancellationToken).GetAsyncEnumerator(cancellationToken);

        // The first event needs retrieval and the model's first token; if either fails, the
        // exception reaches the exception handler while a proper error status can still be sent.
        var hasEvent = await events.MoveNextAsync();

        Response.StatusCode = StatusCodes.Status200OK;
        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no"; // stop reverse proxies from buffering the stream

        try
        {
            while (hasEvent)
            {
                await WriteAsync(events.Current, cancellationToken);
                hasEvent = await events.MoveNextAsync();
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await WriteEventAsync("error", new { title = "The answer could not be completed.", detail = ex.Message }, CancellationToken.None);
        }
    }

    private Task WriteAsync(AnswerStreamEvent item, CancellationToken cancellationToken) =>
        item switch
        {
            AnswerStreamEvent.Sources s => WriteEventAsync("sources", s.Items, cancellationToken),
            AnswerStreamEvent.Token t => WriteEventAsync("token", new { text = t.Text }, cancellationToken),
            AnswerStreamEvent.Done d => WriteEventAsync("done", d.Response, cancellationToken),
            _ => Task.CompletedTask,
        };

    private async Task WriteEventAsync<T>(string name, T payload, CancellationToken cancellationToken)
    {
        // JSON never contains a raw newline, so each event's data fits on one "data:" line.
        await Response.WriteAsync($"event: {name}\ndata: {JsonSerializer.Serialize(payload, Json)}\n\n", cancellationToken);
        await Response.Body.FlushAsync(cancellationToken);
    }
}

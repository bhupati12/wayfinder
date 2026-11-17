using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using SearchService.Application.Filters;

namespace SearchService.Tests.UnitTests;

public sealed class DisableRequestTimeoutOnResultFilterTests
{
    [Fact]
    public async Task Timeout_is_switched_off_before_the_result_is_written()
    {
        var timeout = new FakeTimeoutFeature();
        var http = new DefaultHttpContext();
        http.Features.Set<IHttpRequestTimeoutFeature>(timeout);
        var context = new ResultExecutingContext(
            new ActionContext(http, new RouteData(), new ActionDescriptor()),
            [],
            new OkResult(),
            controller: new object()
        );
        var disabledWhenWriting = false;

        await new DisableRequestTimeoutOnResultFilter().OnResultExecutionAsync(context, () =>
        {
            disabledWhenWriting = timeout.Disabled;
            return Task.FromResult<ResultExecutedContext>(null!);
        });

        Assert.True(disabledWhenWriting);
    }

    [Fact]
    public async Task Requests_without_a_timeout_pass_through()
    {
        var context = new ResultExecutingContext(
            new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor()),
            [],
            new OkResult(),
            controller: new object()
        );
        var called = false;

        await new DisableRequestTimeoutOnResultFilter().OnResultExecutionAsync(context, () =>
        {
            called = true;
            return Task.FromResult<ResultExecutedContext>(null!);
        });

        Assert.True(called);
    }

    private sealed class FakeTimeoutFeature : IHttpRequestTimeoutFeature
    {
        public bool Disabled { get; private set; }

        public CancellationToken RequestTimeoutToken => CancellationToken.None;

        public void DisableTimeout() => Disabled = true;
    }
}

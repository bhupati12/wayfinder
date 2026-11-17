namespace SearchService;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Error tracking: only when a DSN is configured (Sentry__Dsn), so local runs send nothing.
        if (!string.IsNullOrWhiteSpace(builder.Configuration["Sentry:Dsn"]))
        {
            builder.WebHost.UseSentry();
        }

        Startup.ConfigureServices(builder);

        var app = builder.Build();
        Startup.Configure(app);

        await app.RunAsync();
    }
}

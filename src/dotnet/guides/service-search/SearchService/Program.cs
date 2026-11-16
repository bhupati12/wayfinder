namespace SearchService;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        Startup.ConfigureServices(builder);

        var app = builder.Build();
        Startup.Configure(app);

        await app.RunAsync();
    }
}

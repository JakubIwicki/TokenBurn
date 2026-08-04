using Api.TokenBurn.Ingest.Extensions;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args).AddApiServices();
WebApplication app = builder.Build().MapDefaultEndpoints();
await app.InitializeIngestAsync();
app.Run();

public partial class Program;

using Api.TokenBurn.Ingest.Extensions;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args).AddApiServices();
WebApplication app = builder.Build().MapDefaultEndpoints();
app.Run();

public partial class Program;

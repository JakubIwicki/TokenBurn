using Api.TokenBurn.Identity.Extensions;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args).AddApiServices();
WebApplication app = builder.Build();
await app.InitializeIdentityAsync();
app.MapDefaultEndpoints();
app.Run();

public partial class Program;

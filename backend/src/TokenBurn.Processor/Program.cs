using TokenBurn.Processor;

var builder = WebApplication.CreateBuilder(args).AddProcessorServices();
var app = builder.Build().MapProcessorEndpoints();
app.Run();

public partial class Program;

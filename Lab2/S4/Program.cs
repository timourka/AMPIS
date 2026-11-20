using Lab2;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<ActionHandler>(Reports.Apply);
Shared.Start(builder, consume: true);
var app = builder.Build();
app.MapGet("/health", () => Results.Ok());
app.MapGet("/api/files", Reports.List);
app.MapGet("/api/statistics/{id:guid}", Reports.Statistics);
app.Run();
public partial class Program;

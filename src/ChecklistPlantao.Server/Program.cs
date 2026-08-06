var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapControllers();

app.Run();

/// <summary>
/// Exposto para permitir que <c>WebApplicationFactory&lt;Program&gt;</c> encontre o host nos testes de integração.
/// </summary>
public partial class Program;

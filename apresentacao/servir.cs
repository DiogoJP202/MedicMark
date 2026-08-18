#:sdk Microsoft.NET.Sdk.Web
#:property PublishAot=false

// Servidor estático mínimo para abrir a apresentação no navegador.
//
// Existe porque o `file://` funciona no navegador comum, mas não em ferramentas que carregam a
// página como data URL. Use:  dotnet run servir.cs   →   http://localhost:5173

using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

var raiz = Path.GetDirectoryName(Path.GetFullPath(Environment.GetCommandLineArgs()[0]))!;

if (!File.Exists(Path.Combine(raiz, "index.html")))
{
    raiz = Directory.GetCurrentDirectory();
}

var arquivos = new PhysicalFileProvider(raiz);

app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = arquivos });
app.UseStaticFiles(new StaticFileOptions { FileProvider = arquivos, ServeUnknownFileTypes = true });

app.Run("http://localhost:5173");

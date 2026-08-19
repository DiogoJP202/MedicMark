using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace ChecklistPlantao.Server.IntegrationTests;

/// <summary>
/// Configuração inválida precisa derrubar a SUBIDA, com mensagem legível — e não falhar na
/// primeira requisição, quando alguém já depende do servidor.
///
/// O `Database` tinha `ValidateOnStart()` sem `ValidateDataAnnotations()`: a chamada existia e não
/// verificava nada. `Bootstrap` e `Maintenance` não tinham validação alguma.
/// </summary>
public sealed class ConfigurationValidationTests
{
    /// <summary>
    /// Sobe um servidor com a configuração alterada e devolve o que aconteceu. `CreateClient` é o
    /// que força a construção do host — sem ele, a validação de subida não roda.
    /// </summary>
    private static Exception? SubirCom(Dictionary<string, string?> configuracao)
    {
        using var fabrica = new ServidorConfiguravel(configuracao);

        try
        {
            using var cliente = fabrica.CreateClient();
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    [Fact]
    public void Retencao_negativa_derruba_a_subida()
    {
        var erro = SubirCom(new() { ["Maintenance:ChangeLogRetentionDays"] = "-1" });

        Assert.IsType<OptionsValidationException>(erro);
        Assert.Contains("ChangeLogRetentionDays", erro.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Intervalo_de_manutencao_fora_da_faixa_derruba_a_subida()
    {
        var erro = SubirCom(new() { ["Maintenance:IntervalMinutes"] = "0" });

        Assert.IsType<OptionsValidationException>(erro);
        Assert.Contains("IntervalMinutes", erro.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Caminho_do_banco_vazio_derruba_a_subida()
    {
        var erro = SubirCom(new() { ["Database:Path"] = "" });

        Assert.IsType<OptionsValidationException>(erro);
        Assert.Contains("Path", erro.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// O caso mais traiçoeiro: preencher só metade do par não criava administrador nenhum, o
    /// servidor subia normalmente, e a pessoa só descobria no primeiro login.
    /// </summary>
    [Fact]
    public void Usuario_do_administrador_sem_senha_derruba_a_subida()
    {
        var erro = SubirCom(new() { ["Bootstrap:AdminPassword"] = null });

        Assert.IsType<OptionsValidationException>(erro);
        Assert.Contains("juntos", erro.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Nenhum dos dois é configuração legítima: o servidor sobe e não cria ninguém.</summary>
    [Fact]
    public void Sem_nenhum_dos_dois_o_servidor_sobe()
    {
        var erro = SubirCom(new()
        {
            ["Bootstrap:AdminUserName"] = null,
            ["Bootstrap:AdminPassword"] = null,
        });

        Assert.Null(erro);
    }

    /// <summary>Guarda contra falso positivo: a configuração da suíte precisa continuar subindo.</summary>
    [Fact]
    public void Configuracao_valida_sobe_normalmente()
    {
        Assert.Null(SubirCom([]));
    }

    /// <summary>
    /// Mesma base da <see cref="ChecklistServerFactory"/>, com um dicionário aplicado por cima.
    /// Não herda dela porque cada teste precisa de um host próprio que pode falhar ao subir.
    /// </summary>
    private sealed class ServidorConfiguravel(Dictionary<string, string?> alteracoes) : WebApplicationFactory<Program>
    {
        private readonly string _pasta = Path.Combine(
            Path.GetTempPath(), "checklistplantao-tests", Guid.CreateVersion7().ToString("N"));

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            Directory.CreateDirectory(_pasta);
            builder.UseEnvironment("Testing");

            var configuracao = new Dictionary<string, string?>
            {
                ["Database:Path"] = Path.Combine(_pasta, "testes.db"),
                ["Database:EnableWriteAheadLogging"] = "false",
                ["Jwt:SigningKey"] = "chave-exclusiva-de-teste-com-mais-de-32-caracteres-000000",
                ["Bootstrap:AdminUserName"] = ChecklistServerFactory.AdminUserName,
                ["Bootstrap:AdminPassword"] = ChecklistServerFactory.AdminPassword,
                ["Maintenance:IntervalMinutes"] = "1440",
            };

            foreach (var (chave, valor) in alteracoes)
            {
                configuracao[chave] = valor;
            }

            builder.ConfigureAppConfiguration((_, fontes) => fontes.AddInMemoryCollection(configuracao));
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            try
            {
                if (disposing && Directory.Exists(_pasta))
                {
                    Directory.Delete(_pasta, recursive: true);
                }
            }
            catch (IOException)
            {
                // Pasta temporária: o sistema limpa depois.
            }
        }
    }
}

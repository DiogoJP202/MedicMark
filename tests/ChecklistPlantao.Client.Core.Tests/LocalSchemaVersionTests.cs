using ChecklistPlantao.Client.Core.Persistence;
using ChecklistPlantao.Client.Core.Sync;
using ChecklistPlantao.Domain.Sync;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace ChecklistPlantao.Client.Core.Tests;

/// <summary>
/// O banco do aparelho não usa migrations (D-017): <c>EnsureCreated</c> cria o esquema se o arquivo
/// não existir e **não faz nada** se existir com o esquema antigo.
///
/// Sem detecção de versão, a próxima mudança nas entidades locais deixaria os aparelhos já
/// instalados com a tabela velha, falhando em execução com erro obscuro. É a classe de defeito que
/// mais custou neste projeto — descoberta duas vezes, em campo, pelo sintoma errado.
///
/// Estes testes fixam as três respostas possíveis da subida.
/// </summary>
public sealed class LocalSchemaVersionTests : IDisposable
{
    private readonly string _arquivo = Path.Combine(Path.GetTempPath(), $"checklist-esquema-{Guid.CreateVersion7():N}.db");

    private LocalDbContext CriarContexto()
    {
        var opcoes = new DbContextOptionsBuilder<LocalDbContext>()
            .UseSqlite($"Data Source={_arquivo}")
            .Options;

        return new LocalDbContext(opcoes);
    }

    private static int LerVersao(LocalDbContext db)
    {
        var conexao = db.Database.GetDbConnection();

        if (conexao.State != System.Data.ConnectionState.Open)
        {
            conexao.Open();
        }

        using var comando = conexao.CreateCommand();
        comando.CommandText = "PRAGMA user_version";

        return Convert.ToInt32(comando.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static void GravarVersao(LocalDbContext db, int versao)
    {
        var conexao = db.Database.GetDbConnection();

        if (conexao.State != System.Data.ConnectionState.Open)
        {
            conexao.Open();
        }

        using var comando = conexao.CreateCommand();
        comando.CommandText = "PRAGMA user_version = " + versao;

        comando.ExecuteNonQuery();
    }

    [Fact]
    public void Banco_novo_nasce_com_a_versao_atual()
    {
        using var db = CriarContexto();

        DependencyInjection.EnsureLocalSchema(db, NullLogger.Instance);

        Assert.Equal(LocalDbContext.LocalSchemaVersion, LerVersao(db));
    }

    /// <summary>Abertura normal do aplicativo: nada pode ser destruído.</summary>
    [Fact]
    public void Versao_igual_preserva_os_dados()
    {
        using (var primeira = CriarContexto())
        {
            DependencyInjection.EnsureLocalSchema(primeira, NullLogger.Instance);

            primeira.Outbox.Add(new SyncOutboxItem(
                Guid.CreateVersion7(), SyncEntityTypes.ChecklistEntry, Guid.CreateVersion7(),
                SyncOperationType.Upsert, "{}", 0, DateTime.UtcNow));

            primeira.SaveChanges();
        }

        using var segunda = CriarContexto();
        DependencyInjection.EnsureLocalSchema(segunda, NullLogger.Instance);

        Assert.Equal(1, segunda.Outbox.Count());
    }

    /// <summary>
    /// O caso que justifica tudo: o aplicativo subiu com o modelo novo sobre um arquivo antigo.
    /// O banco é recriado, e o cursor volta a zero para o bootstrap trazer tudo do servidor.
    /// </summary>
    [Fact]
    public void Versao_diferente_recria_o_banco()
    {
        using (var antiga = CriarContexto())
        {
            DependencyInjection.EnsureLocalSchema(antiga, NullLogger.Instance);

            antiga.Outbox.Add(new SyncOutboxItem(
                Guid.CreateVersion7(), SyncEntityTypes.ChecklistEntry, Guid.CreateVersion7(),
                SyncOperationType.Upsert, "{}", 0, DateTime.UtcNow));

            var estado = new SyncState();
            estado.MarkBootstrapped(42, DateTime.UtcNow);
            antiga.SyncState.Add(estado);

            antiga.SaveChanges();

            // Simula um aparelho que ficou para trás.
            GravarVersao(antiga, LocalDbContext.LocalSchemaVersion - 1);
        }

        using var nova = CriarContexto();
        DependencyInjection.EnsureLocalSchema(nova, NullLogger.Instance);

        Assert.Equal(LocalDbContext.LocalSchemaVersion, LerVersao(nova));
        Assert.Empty(nova.Outbox);

        // Sem estado de sincronização, o bootstrap é obrigatório na próxima conexão — que é
        // exatamente como o banco local se reconstitui.
        Assert.Empty(nova.SyncState);
    }

    /// <summary>Recriar não pode deixar o esquema pela metade: o banco novo tem de ser usável.</summary>
    [Fact]
    public void Banco_recriado_aceita_gravacao()
    {
        using (var antiga = CriarContexto())
        {
            DependencyInjection.EnsureLocalSchema(antiga, NullLogger.Instance);
            GravarVersao(antiga, LocalDbContext.LocalSchemaVersion + 7);
        }

        using var nova = CriarContexto();
        DependencyInjection.EnsureLocalSchema(nova, NullLogger.Instance);

        nova.DeviceState.Add(new DeviceState());
        nova.SaveChanges();

        Assert.Equal(1, nova.DeviceState.Count());
    }

    public void Dispose()
    {
        foreach (var caminho in new[] { _arquivo, _arquivo + "-wal", _arquivo + "-shm" })
        {
            try
            {
                if (File.Exists(caminho))
                {
                    File.Delete(caminho);
                }
            }
            catch (IOException)
            {
                // Arquivo temporário: o sistema limpa depois.
            }
        }
    }
}

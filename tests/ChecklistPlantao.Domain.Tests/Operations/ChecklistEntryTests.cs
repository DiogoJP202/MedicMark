using ChecklistPlantao.Domain.Operations;

namespace ChecklistPlantao.Domain.Tests.Operations;

public sealed class ChecklistEntryTests
{
    private static ChecklistEntry NovaEntrada() => new(
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        TestData.NowUtc);

    [Fact]
    public void Entrada_nasce_pendente()
    {
        var entrada = NovaEntrada();

        Assert.False(entrada.IsCompleted);
        Assert.Equal(1, entrada.Version);
    }

    [Fact]
    public void Marcar_altera_o_estado_e_avanca_a_versao()
    {
        var entrada = NovaEntrada();

        var mudou = entrada.SetCompletion(true, TestData.NowUtc.AddMinutes(1));

        Assert.True(mudou);
        Assert.True(entrada.IsCompleted);
        Assert.Equal(2, entrada.Version);
    }

    [Fact]
    public void Marcar_de_novo_nao_avanca_a_versao()
    {
        var entrada = NovaEntrada();
        entrada.SetCompletion(true, TestData.NowUtc);

        var mudou = entrada.SetCompletion(true, TestData.NowUtc.AddMinutes(5));

        Assert.False(mudou);
        Assert.Equal(2, entrada.Version);
    }

    [Fact]
    public void Desmarcar_volta_ao_pendente()
    {
        var entrada = NovaEntrada();
        entrada.SetCompletion(true, TestData.NowUtc);

        entrada.SetCompletion(false, TestData.NowUtc.AddMinutes(1));

        Assert.False(entrada.IsCompleted);
        Assert.Equal(3, entrada.Version);
    }

    [Fact]
    public void Estado_do_servidor_substitui_o_local()
    {
        var entrada = NovaEntrada();
        var instante = TestData.NowUtc.AddHours(2);

        entrada.OverwriteFromServer(completed: true, version: 42, updatedAtUtc: instante);

        Assert.True(entrada.IsCompleted);
        Assert.Equal(42, entrada.Version);
        Assert.Equal(instante, entrada.UpdatedAtUtc);
    }

    [Fact]
    public void Progresso_conta_concluidas_e_pendentes()
    {
        var entradas = Enumerable.Range(0, 5).Select(_ => NovaEntrada()).ToList();
        entradas[0].SetCompletion(true, TestData.NowUtc);
        entradas[1].SetCompletion(true, TestData.NowUtc);

        var progresso = ChecklistProgress.From(entradas);

        Assert.Equal(5, progresso.Total);
        Assert.Equal(2, progresso.Completed);
        Assert.Equal(3, progresso.Pending);
        Assert.Equal(40, progresso.Percent);
        Assert.False(progresso.IsComplete);
    }

    [Fact]
    public void Progresso_sem_tarefas_conta_como_completo()
    {
        var progresso = ChecklistProgress.Empty;

        Assert.True(progresso.IsComplete);
        Assert.Equal(100, progresso.Percent);
    }

    [Fact]
    public void Progressos_podem_ser_somados()
    {
        var soma = new ChecklistProgress(10, 4) + new ChecklistProgress(6, 6);

        Assert.Equal(16, soma.Total);
        Assert.Equal(10, soma.Completed);
    }
}

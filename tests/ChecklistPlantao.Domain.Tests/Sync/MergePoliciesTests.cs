using ChecklistPlantao.Domain.Sync;

namespace ChecklistPlantao.Domain.Tests.Sync;

public sealed class MergePoliciesTests
{
    [Fact]
    public void Marcar_o_que_ja_esta_marcado_e_sucesso_idempotente()
    {
        var decisao = MergePolicies.ResolveChecklistEntry(
            serverIsCompleted: true, serverVersion: 5, requestedIsCompleted: true, baseVersion: 1);

        Assert.Equal(MergeDecision.AlreadyInDesiredState, decisao);
    }

    [Fact]
    public void Desmarcar_o_que_ja_esta_desmarcado_e_sucesso_idempotente()
    {
        var decisao = MergePolicies.ResolveChecklistEntry(
            serverIsCompleted: false, serverVersion: 5, requestedIsCompleted: false, baseVersion: 1);

        Assert.Equal(MergeDecision.AlreadyInDesiredState, decisao);
    }

    [Fact]
    public void Marcar_com_versao_atual_e_aplicado()
    {
        var decisao = MergePolicies.ResolveChecklistEntry(
            serverIsCompleted: false, serverVersion: 3, requestedIsCompleted: true, baseVersion: 3);

        Assert.Equal(MergeDecision.Apply, decisao);
    }

    [Fact]
    public void Marcar_com_versao_antiga_ainda_e_aplicado_porque_conclusao_vence()
    {
        var decisao = MergePolicies.ResolveChecklistEntry(
            serverIsCompleted: false, serverVersion: 9, requestedIsCompleted: true, baseVersion: 1);

        Assert.Equal(MergeDecision.Apply, decisao);
    }

    [Fact]
    public void Desmarcar_com_versao_atual_e_aplicado()
    {
        var decisao = MergePolicies.ResolveChecklistEntry(
            serverIsCompleted: true, serverVersion: 4, requestedIsCompleted: false, baseVersion: 4);

        Assert.Equal(MergeDecision.Apply, decisao);
    }

    [Fact]
    public void Desmarcar_offline_nao_apaga_conclusao_mais_nova()
    {
        // Dispositivo ficou offline vendo a versão 2; enquanto isso outro aparelho concluiu (versão 7).
        var decisao = MergePolicies.ResolveChecklistEntry(
            serverIsCompleted: true, serverVersion: 7, requestedIsCompleted: false, baseVersion: 2);

        Assert.Equal(MergeDecision.KeepServerState, decisao);
    }

    [Fact]
    public void Versao_base_adiantada_e_tratada_como_aplicavel()
    {
        // Cenário defensivo: não deve ocorrer, mas se ocorrer não pode travar o cliente.
        var decisao = MergePolicies.ResolveChecklistEntry(
            serverIsCompleted: true, serverVersion: 2, requestedIsCompleted: false, baseVersion: 5);

        Assert.Equal(MergeDecision.Apply, decisao);
    }

    [Fact]
    public void Configuracao_nao_usa_conclusao_vence_e_recusa_versao_antiga()
    {
        var decisao = MergePolicies.ResolveVersioned(serverVersion: 8, baseVersion: 3, wouldChangeState: true);

        Assert.Equal(MergeDecision.KeepServerState, decisao);
    }

    [Fact]
    public void Configuracao_com_versao_atual_e_aplicada()
    {
        var decisao = MergePolicies.ResolveVersioned(serverVersion: 8, baseVersion: 8, wouldChangeState: true);

        Assert.Equal(MergeDecision.Apply, decisao);
    }

    [Fact]
    public void Configuracao_sem_mudanca_e_idempotente_mesmo_com_versao_antiga()
    {
        var decisao = MergePolicies.ResolveVersioned(serverVersion: 8, baseVersion: 1, wouldChangeState: false);

        Assert.Equal(MergeDecision.AlreadyInDesiredState, decisao);
    }
}

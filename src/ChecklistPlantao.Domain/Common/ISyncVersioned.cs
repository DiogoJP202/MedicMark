namespace ChecklistPlantao.Domain.Common;

/// <summary>
/// Entidade que participa da sincronização e por isso carrega versão otimista.
/// A versão é sempre atribuída pelo servidor; o cliente apenas guarda a última que conhece
/// e a devolve como <c>BaseVersion</c> ao enviar uma alteração.
/// </summary>
public interface ISyncVersioned
{
    Guid Id { get; }

    int Version { get; }

    DateTime UpdatedAtUtc { get; }
}

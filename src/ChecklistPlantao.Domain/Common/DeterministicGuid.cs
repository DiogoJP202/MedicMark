using System.Security.Cryptography;
using System.Text;

namespace ChecklistPlantao.Domain.Common;

/// <summary>
/// Gera identificadores estáveis a partir de um nome lógico.
///
/// Serve exclusivamente para dados de seed: servidor e clientes chegam ao mesmo Id sem precisar
/// negociar nada, e rodar o seed duas vezes não duplica registro. Dados criados em tempo de
/// execução usam <see cref="Guid.CreateVersion7"/>, que é ordenável no tempo e melhor para índices.
/// </summary>
public static class DeterministicGuid
{
    public static Guid From(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes("checklistplantao/" + name));
        return new Guid(hash.AsSpan(0, 16));
    }
}

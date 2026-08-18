namespace ChecklistPlantao.UI.Components;

/// <summary>
/// Uma entrada do menu administrativo.
///
/// A permissão viaja junto com o item porque quem decide o que aparece é o acesso do usuário,
/// não a tela: não existe "modo admin" que libera tudo de uma vez.
/// </summary>
/// <param name="Title">Rótulo do cartão.</param>
/// <param name="Description">Uma linha explicando o que há do outro lado.</param>
/// <param name="Route">Rota de destino.</param>
/// <param name="Permission">Chave exigida para o item aparecer.</param>
public sealed record AdminMenuItem(string Title, string Description, string Route, string Permission);

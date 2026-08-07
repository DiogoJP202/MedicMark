namespace ChecklistPlantao.Client;

/// <summary>
/// Aplicativo MAUI.
///
/// <c>Application</c> é qualificado porque o projeto também referencia o namespace
/// <c>ChecklistPlantao.Application</c>, e de dentro de <c>ChecklistPlantao.Client</c> o nome
/// curto resolveria para o namespace errado.
/// </summary>
public partial class App : Microsoft.Maui.Controls.Application
{
    public App()
    {
        InitializeComponent();
    }

    protected override Window CreateWindow(IActivationState? activationState) =>
        new(new MainPage()) { Title = "Checklist de Plantão" };
}

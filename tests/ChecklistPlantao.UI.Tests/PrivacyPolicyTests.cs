using Bunit;
using ChecklistPlantao.UI.Pages;

namespace ChecklistPlantao.UI.Tests;

public sealed class PrivacyPolicyTests : BunitContext
{
    [Fact]
    public void Policy_states_that_patient_data_is_not_collected()
    {
        var cut = Render<PrivacyPolicyPage>();

        Assert.Contains("Nenhum dado de paciente", cut.Markup);
        Assert.Contains("Não vendemos dados", cut.Markup);
    }
}

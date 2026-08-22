using ChecklistPlantao.Client.Core.Notifications;

namespace ChecklistPlantao.Client.Core.Tests;

public sealed class NotificationNavigationTests
{
    [Fact]
    public void Rota_interna_fica_pendente_ate_o_webview_confirmar()
    {
        ClearPendingRoute();
        const string route = "/checklist/11111111-1111-1111-1111-111111111111/22222222-2222-2222-2222-222222222222";

        NotificationNavigation.Request(route);

        Assert.Equal(route, NotificationNavigation.PendingRoute);

        NotificationNavigation.Complete(route);

        Assert.Null(NotificationNavigation.PendingRoute);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("checklist/sem-barra")]
    [InlineData("//outro-host/caminho")]
    [InlineData("https://exemplo.invalid/checklist")]
    public void Rota_externa_ou_invalida_e_ignorada(string? route)
    {
        ClearPendingRoute();

        NotificationNavigation.Request(route);

        Assert.Null(NotificationNavigation.PendingRoute);
    }

    private static void ClearPendingRoute()
    {
        var pending = NotificationNavigation.PendingRoute;

        if (pending is not null)
        {
            NotificationNavigation.Complete(pending);
        }
    }
}

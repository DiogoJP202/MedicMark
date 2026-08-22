using ChecklistPlantao.Client.Core.Notifications;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace ChecklistPlantao.Client;

public partial class MainPage : ContentPage
{
    private readonly SemaphoreSlim _navigationGate = new(1, 1);
    private CancellationTokenSource? _navigationCancellation;

    public MainPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object? sender, EventArgs e)
    {
        _navigationCancellation?.Cancel();
        _navigationCancellation?.Dispose();
        _navigationCancellation = new CancellationTokenSource();

        NotificationNavigation.RouteRequested -= OnRouteRequested;
        NotificationNavigation.RouteRequested += OnRouteRequested;
        _ = NavigateToPendingNotificationAsync(_navigationCancellation.Token);
    }

    private void OnUnloaded(object? sender, EventArgs e)
    {
        NotificationNavigation.RouteRequested -= OnRouteRequested;
        _navigationCancellation?.Cancel();
        _navigationCancellation?.Dispose();
        _navigationCancellation = null;
    }

    private void OnRouteRequested() =>
        _ = NavigateToPendingNotificationAsync(_navigationCancellation?.Token ?? CancellationToken.None);

    private async Task NavigateToPendingNotificationAsync(CancellationToken cancellationToken)
    {
        var lockTaken = false;

        try
        {
            await _navigationGate.WaitAsync(cancellationToken);
            lockTaken = true;

            for (var attempt = 0; attempt < 10; attempt++)
            {
                var route = NotificationNavigation.PendingRoute;

                if (route is null)
                {
                    return;
                }

                var dispatched = await blazorWebView.TryDispatchAsync(services =>
                {
                    services.GetRequiredService<NavigationManager>().NavigateTo(route);
                });

                if (dispatched)
                {
                    NotificationNavigation.Complete(route);
                    return;
                }

                await Task.Delay(200, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // A página saiu da árvore visual; a rota continua pendente para a próxima abertura.
        }
        finally
        {
            if (lockTaken)
            {
                _navigationGate.Release();
            }
        }
    }
}

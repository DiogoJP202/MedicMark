namespace ChecklistPlantao.Client.Core.Notifications;

/// <summary>
/// Faz a ponte entre o toque numa notificação nativa e o roteador Blazor Hybrid.
/// A rota fica pendente até o WebView confirmar que conseguiu recebê-la, inclusive quando o
/// aplicativo foi aberto a partir de um estado totalmente encerrado.
/// </summary>
public static class NotificationNavigation
{
    private static readonly object Gate = new();
    private static string? _pendingRoute;

    public static event Action? RouteRequested;

    public static string? PendingRoute
    {
        get
        {
            lock (Gate)
            {
                return _pendingRoute;
            }
        }
    }

    public static void Request(string? route)
    {
        if (string.IsNullOrWhiteSpace(route)
            || !route.StartsWith("/", StringComparison.Ordinal)
            || route.StartsWith("//", StringComparison.Ordinal)
            || !Uri.TryCreate(route, UriKind.Relative, out _))
        {
            return;
        }

        lock (Gate)
        {
            _pendingRoute = route;
        }

        RouteRequested?.Invoke();
    }

    public static void Complete(string route)
    {
        lock (Gate)
        {
            if (string.Equals(_pendingRoute, route, StringComparison.Ordinal))
            {
                _pendingRoute = null;
            }
        }
    }
}

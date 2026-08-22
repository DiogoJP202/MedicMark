using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.View;
using ChecklistPlantao.Client.Core.Notifications;
using ChecklistPlantao.Client.Platforms.Android;

namespace ChecklistPlantao.Client;

[Activity(
    Theme = "@style/Maui.SplashTheme",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        // Impede o conteúdo de desenhar sob a barra de status.
        //
        // Sem isto, o topo da página fica escondido atrás da barra do sistema: o título some e,
        // pior, o botão de recolher o aviso de notificações — que fica no alto da faixa fixa —
        // vira intocável. O usuário vê um aviso que não consegue fechar.
        //
        // A folha de estilo já reserva env(safe-area-inset-top), mas isso só resolve quando o
        // WebView de fato reporta a área segura. Garantir aqui torna o comportamento
        // independente de fabricante — e a MIUI é conhecida por divergir do padrão.
        if (Window is { } janela)
        {
            WindowCompat.SetDecorFitsSystemWindows(janela, true);
        }

        NotificationNavigation.Request(Intent?.GetStringExtra(AndroidNotificationScheduler.ExtraRoute));
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        NotificationNavigation.Request(intent?.GetStringExtra(AndroidNotificationScheduler.ExtraRoute));
    }
}

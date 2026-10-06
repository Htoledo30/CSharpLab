using System.Globalization;
using System.Windows;
using System.Windows.Threading;
using CSharpLab.Core.Settings;
using CSharpLab.ViewModels;

namespace CSharpLab;

public partial class App : Application
{
    private InstanceLease? _instanceLease;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("pt-BR");

        DispatcherUnhandledException += OnDispatcherException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex) AppPaths.Log(ex, "Erro não tratado");
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            AppPaths.Log(args.Exception, "Tarefa sem observação");
            args.SetObserved();
        };

        try
        {
            _instanceLease = InstanceLease.TryAcquire(AppPaths.Root);
            if (_instanceLease == null)
            {
                MessageBox.Show("O CSharp Lab já está aberto.", "CSharp Lab", MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown();
                return;
            }
            var settings = SettingsStore.Load();
            var vm = new MainViewModel(settings);
            var window = new MainWindow(vm);
            MainWindow = window;
            window.Show();
        }
        catch (Exception ex)
        {
            AppPaths.Log(ex, "Abrindo a janela");
            MessageBox.Show("Não foi possível abrir o CSharp Lab.\n\n" + ex.Message, "CSharp Lab", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _instanceLease?.Dispose();
        base.OnExit(e);
    }

    private void OnDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Um erro inesperado não pode derrubar o editor nem perder texto: registra e segue.
        AppPaths.Log(e.Exception, "Erro na interface");
        e.Handled = true;
        if (MainWindow?.DataContext is MainViewModel vm)
            vm.NotifyError("Algo deu errado, mas o seu texto está seguro. Detalhes em " + AppPaths.LogFile);
    }
}

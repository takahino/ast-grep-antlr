using AstGrepAntlr.Gui.Services;
using AstGrepAntlr.Gui.ViewModels;
using AstGrepAntlr.Gui.Views;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace AstGrepAntlr.Gui;

public sealed partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var store = new SettingsStore();
            var vm = new MainViewModel(store, new SearchService());
            var window = new MainWindow { DataContext = vm };
            window.ApplySettings(vm.Settings);
            desktop.MainWindow = window;
            desktop.ShutdownRequested += (_, _) => vm.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }
}

using Avalonia.Interactivity;
using YoutubeDownloader.Framework;
using YoutubeDownloader.ViewModels;

namespace YoutubeDownloader.Views;

public partial class MainView : Window<MainViewModel>
{
    private bool _closing;
    private bool _readyToClose;

    public MainView()
    {
        InitializeComponent();
        Closing += async (_, args) =>
        {
            if (_readyToClose)
                return;
            args.Cancel = true;
            if (_closing)
                return;
            _closing = true;
            try
            {
                await DataContext.ShutdownAsync();
            }
            finally
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    _readyToClose = true;
                    Close();
                });
            }
        };
    }

    private void DialogHost_OnLoaded(object? sender, RoutedEventArgs args) =>
        DataContext.InitializeCommand.Execute(null);
}

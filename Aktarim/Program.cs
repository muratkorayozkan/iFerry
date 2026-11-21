using System.IO;
using System.Windows.Media.Imaging;
using Microsoft.AspNetCore.Builder;
using QRCoder;
using System.Windows;
using System.Windows.Threading;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Principal;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        var app = new Application();
        app.Run(new TransferWindow());
    }
}

internal partial class TransferWindow : Window
{
    readonly string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "iPhoneAktarim");
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(4) };
    WebApplication? browserServer;

    public TransferWindow()
    {
        InitializeComponent();
        browserSession.Click += async (_, _) => await ToggleBrowserAsync();
        account.Click += async (_, _) =>
        {
            account.IsEnabled = false;
            try
            {
                var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = true, Verb = "runas" };
                foreach (var arg in new[] { "-NoProfile", "-File", Path.Combine(AppContext.BaseDirectory, "SMB-Hesap.ps1"), "-Folder", folder })
                    start.ArgumentList.Add(arg);
                using var process = Process.Start(start)!;
                await process.WaitForExitAsync();
                MessageBox.Show(process.ExitCode == 0 ? $"Kullanıcı adı: {Environment.MachineName}\\iPhoneTransfer" :
                    "Hesap oluşturulamadı. Daha önce oluşturduysanız mevcut parolayla bağlanabilirsiniz.", Title);
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, Title); }
            finally { account.IsEnabled = true; }
        };
        open.Click += (_, _) => Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
        setup.Click += async (_, _) => await ConfigureAsync();
        timer.Tick += (_, _) => RefreshState();
        Loaded += (_, _) => { RefreshState(); timer.Start(); };
        Closed += (_, _) => timer.Stop();
    }

    async Task ToggleBrowserAsync()
    {
        browserSession.IsEnabled = false;
        try
        {
            if (browserServer is not null)
            {
                await browserServer.DisposeAsync();
                browserServer = null;
                browserSession.Content = "Oturumu başlat";
                browserAddress.Visibility = browserQr.Visibility = Visibility.Collapsed;
                browserStatus.Text = "Oturum kapatıldı.";
                return;
            }
            var ip = Wifi()?.GetIPProperties().UnicastAddresses
                .FirstOrDefault(entry => entry.Address.AddressFamily == AddressFamily.InterNetwork)?.Address
                ?? throw new InvalidOperationException("Önce iPhone’un kişisel erişim noktasına bağlanın.");
            var session = await BrowserTransfer.StartAsync(ip, folder);
            browserServer = session.Server;
            browserSession.Content = "Oturumu kapat";
            browserAddress.Text = session.Address;
            browserAddress.Visibility = Visibility.Visible;
            using var qrData = QRCodeGenerator.GenerateQrCode(session.Address, QRCodeGenerator.ECCLevel.M);
            using var qr = new PngByteQRCode(qrData);
            using var png = new MemoryStream(qr.GetGraphic(5));
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = png;
            bitmap.EndInit();
            browserQr.Source = bitmap;
            browserQr.Visibility = Visibility.Visible;
            browserStatus.Text = "Oturum açık. Dosyalar iPhoneAktarim klasörüne kaydedilir.";
        }
        catch (Exception ex) { browserStatus.Text = ex.Message; }
        finally { browserSession.IsEnabled = true; }
    }

    static NetworkInterface? Wifi() => NetworkInterface.GetAllNetworkInterfaces()
        .FirstOrDefault(n => n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 && n.OperationalStatus == OperationalStatus.Up &&
            n.GetIPProperties().GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork));

    void RefreshState()
    {
        try
        {
            var ip = Wifi()?.GetIPProperties().UnicastAddresses.FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork)?.Address;
            address.Text = ip == null ? "" : $"smb://{ip}/iPhoneAktarim";
            bool shared = Directory.Exists(@"\\localhost\iPhoneAktarim");
            status.Text = ip == null ? "iPhone’un kişisel erişim noktasına bağlanın." :
                shared ? "SMB paylaşımı erişilebilir." : "SMB paylaşımına erişilemiyor.";
            open.IsEnabled = Directory.Exists(folder);
            files.Items.Clear();
            if (Directory.Exists(folder))
                foreach (var file in new DirectoryInfo(folder).EnumerateFiles()
                    .Where(file => !file.Name.EndsWith(".upload", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(file => file.LastWriteTime))
                    files.Items.Add(new IncomingFile(file.Name, file.FullName, file.Length.ToString("N0") + " bayt"));
        }
        catch (Exception ex) { status.Text = ex.Message; }
    }

    sealed record IncomingFile(string Name, string FullPath, string Size);

    private void OpenFile(object sender, RoutedEventArgs args)
    {
        if (((FrameworkElement)sender).DataContext is not IncomingFile file) return;
        try { Process.Start(new ProcessStartInfo(file.FullPath) { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show(ex.Message, Title); }
    }

    async Task ConfigureAsync()
    {
        setup.IsEnabled = false;
        try
        {
            var wifi = Wifi() ?? throw new InvalidOperationException("Önce iPhone’un kişisel erişim noktasına bağlanın.");
            var gateway = wifi.GetIPProperties().GatewayAddresses.First(g => g.Address.AddressFamily == AddressFamily.InterNetwork).Address;
            if (MessageBox.Show($"iPhone’un erişim noktasına bağlıysanız devam edin. {gateway} adresine SMB erişimi açılacak.", Title,
                MessageBoxButton.OKCancel) != MessageBoxResult.OK) return;
            var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = true, Verb = "runas" };
            foreach (var arg in new[] { "-NoProfile", "-ExecutionPolicy", "RemoteSigned", "-File", Path.Combine(AppContext.BaseDirectory, "SMB-Kur.ps1"),
                "-Account", WindowsIdentity.GetCurrent().Name, "-Folder", folder, "-PhoneAddress", gateway.ToString(), "-InterfaceAlias", wifi.Name })
                start.ArgumentList.Add(arg);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Kurulum başlatılamadı.");
            await process.WaitForExitAsync();
            if (process.ExitCode != 0) throw new InvalidOperationException($"Paylaşım kurulamadı (kod {process.ExitCode}).");
            RefreshState();
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, Title); }
        finally { setup.IsEnabled = true; }
    }
}

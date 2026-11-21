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
    readonly string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "iFerry");
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(4) };
    WebApplication? browserServer;
    (string Stage, string Tag)? pendingUpdate;
    bool installing;

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
                foreach (var arg in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", Path.Combine(AppContext.BaseDirectory, "New-SmbTransferAccount.ps1"), "-Folder", folder })
                    start.ArgumentList.Add(arg);
                using var process = Process.Start(start)!;
                await process.WaitForExitAsync();
                ShowNotice(process.ExitCode == 0 ? "Aktarım hesabı hazır" : "Hesap ayarlanamadı",
                    process.ExitCode == 0 ? $"{Environment.MachineName}\\iPhoneTransfer hesabıyla bağlanabilirsiniz." :
                    "SMB ekranında önce paylaşımı yapılandırın ve yönetici onayı vererek tekrar deneyin.");
            }
            catch (Exception ex) { ShowNotice("Hesap ayarlanamadı", ex.Message); }
            finally { account.IsEnabled = true; }
        };
        open.Click += (_, _) => Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
        setup.Click += async (_, _) => await ConfigureAsync();
        timer.Tick += (_, _) => RefreshState();
        update.Click += async (_, _) => await HandleUpdate(true);
        Loaded += async (_, _) => { RefreshState(); timer.Start(); await HandleUpdate(false); };
        Closed += (_, _) =>
        {
            timer.Stop();
            if (!installing && pendingUpdate is { } pending) Directory.Delete(pending.Stage, true);
        };

        async Task HandleUpdate(bool manual)
        {
            update.IsEnabled = false;
            try
            {
                if (pendingUpdate is { } pending)
                {
                    if (browserServer is not null || !setup.IsEnabled || !account.IsEnabled)
                    {
                        updateStatus.Text = "Güncellemeden önce aktarım oturumunu ve SMB kurulumunu tamamlayın.";
                        return;
                    }
                    if (!ShowNotice("Güncelleme hazır", $"{pending.Tag} yüklensin ve iFerry yeniden başlatılsın mı?", "Güncelle")) return;
                    var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true };
                    var updater = Path.Combine(pending.Stage, "ApplyUpdate.ps1");
                    File.Copy(Path.Combine(AppContext.BaseDirectory, "Update-iFerry.ps1"), updater, true);
                    foreach (var arg in new[] { "-NoProfile", "-ExecutionPolicy", "RemoteSigned", "-File", updater,
                        "-Target", Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory), "-AppPid", Environment.ProcessId.ToString() })
                        start.ArgumentList.Add(arg);
                    using var process = Process.Start(start) ?? throw new InvalidOperationException("Güncelleyici başlatılamadı.");
                    installing = true;
                    Close();
                    return;
                }
                updateStatus.Text = "Güncellemeler denetleniyor…";
                pendingUpdate = await Updates.DownloadAsync();
                if (!IsLoaded)
                {
                    if (pendingUpdate is { } staged) Directory.Delete(staged.Stage, true);
                    return;
                }
                updateStatus.Text = pendingUpdate is { } ready ? $"{ready.Tag} kuruluma hazır." : $"iFerry {Updates.Current.ToString(3)} güncel.";
                if (pendingUpdate is not null) update.Content = "Yeniden başlat ve güncelle";
            }
            catch (Exception ex) { updateStatus.Text = manual ? ex.Message : "Güncelleme denetlenemedi."; }
            finally { update.IsEnabled = true; }
        }
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
            browserStatus.Text = "Oturum açık. Dosyalar iFerry klasörüne kaydedilir.";
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
            address.Text = ip == null ? "" : $"smb://{ip}/iFerry";
            bool shared = Directory.Exists(@"\\localhost\iFerry");
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
        catch (Exception ex) { ShowNotice("Dosya açılamadı", ex.Message); }
    }

    async Task ConfigureAsync()
    {
        setup.IsEnabled = false;
        try
        {
            var wifi = Wifi() ?? throw new InvalidOperationException("Önce iPhone’un kişisel erişim noktasına bağlanın.");
            var gateway = wifi.GetIPProperties().GatewayAddresses.First(g => g.Address.AddressFamily == AddressFamily.InterNetwork).Address;
            if (!ShowNotice("SMB paylaşımını yapılandır", $"iPhone’un erişim noktasına bağlıysanız devam edin. {gateway} adresine SMB erişimi açılacak.", "Yapılandır")) return;
            var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = true, Verb = "runas" };
            foreach (var arg in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", Path.Combine(AppContext.BaseDirectory, "Configure-SmbShare.ps1"),
                "-Account", WindowsIdentity.GetCurrent().Name, "-Folder", folder, "-PhoneAddress", gateway.ToString(), "-InterfaceAlias", wifi.Name })
                start.ArgumentList.Add(arg);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Kurulum başlatılamadı.");
            await process.WaitForExitAsync();
            if (process.ExitCode != 0) throw new InvalidOperationException($"Paylaşım kurulamadı (kod {process.ExitCode}).");
            RefreshState();
        }
        catch (Exception ex) { ShowNotice("Paylaşım yapılandırılamadı", ex.Message); }
        finally { setup.IsEnabled = true; }
    }
}

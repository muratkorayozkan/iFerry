using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Windows;

internal partial class TransferWindow
{
    readonly string pairingPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "iFerry", "receive.key");
    string downloadAddress = "";

    void ConfigureTransferActions()
    {
        AllowDrop = true;
        PreviewDragOver += (_, e) =>
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        };
        Drop += async (_, e) =>
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] paths) await SendSelectionAsync(paths);
        };
        remember.IsChecked = File.Exists(pairingPath);
        forget.Click += (_, _) =>
        {
            try { File.Delete(pairingPath); remember.IsChecked = false; browserStatus.Text = "Kayıtlı bağlantı kaldırıldı."; }
            catch (Exception ex) { ShowNotice("Eşleşme kaldırılamadı", ex.Message); }
        };
        explorer.Click += (_, _) =>
        {
            try
            {
                var directory = Environment.GetFolderPath(Environment.SpecialFolder.SendTo);
                dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell", true)!)!;
                dynamic link = shell.CreateShortcut(Path.Combine(directory, "iFerry.lnk"));
                link.TargetPath = Environment.ProcessPath!;
                link.WorkingDirectory = AppContext.BaseDirectory;
                link.Save();
                sendStatus.Text = "Gezginde dosyaları seçip Gönder → iFerry yolunu kullanın.";
            }
            catch (Exception ex) { ShowNotice("Kısayol oluşturulamadı", ex.Message); }
        };
        listMode.Checked += (_, _) => RefreshDownloadQr();
        listMode.Unchecked += (_, _) => RefreshDownloadQr();
        copyText.Click += (_, _) =>
        {
            try { Clipboard.SetText(messageText.Text); } catch (Exception ex) { ShowNotice("Kopyalanamadı", ex.Message); }
        };
        openLink.Click += (_, _) =>
        {
            if (WebLink(messageText.Text) is not { } uri) return;
            try { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
            catch (Exception ex) { ShowNotice("Açılamadı", ex.Message); }
        };
    }

    string ReadPairing()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(pairingPath)!);
        if (!File.Exists(pairingPath))
        {
            using var file = new FileStream(pairingPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            using var writer = new StreamWriter(file);
            writer.Write(Convert.ToHexString(RandomNumberGenerator.GetBytes(24)));
        }
        var token = File.ReadAllText(pairingPath);
        if (token.Length != 48 || !token.All(Uri.IsHexDigit)) throw new IOException("Eşleşmeyi kaldırıp yeniden oluşturun.");
        return token;
    }

    async Task SendSelectionAsync(string[] paths)
    {
        browser.IsChecked = true;
        if (!send.IsEnabled) return;
        if (paths.Length == 0 || paths.Any(path => !File.Exists(path)))
        {
            sendStatus.Text = "Göndermek için dosyaları seçin.";
            return;
        }
        await ToggleDownloadAsync(paths.Select(Path.GetFullPath).ToArray());
    }

    void RefreshDownloadQr()
    {
        if (downloadServer is not null)
            sendQr.Source = MakeQr(downloadAddress + (listMode.IsChecked == true ? "/list" : ""));
    }

    void Report(string message) => Dispatcher.InvokeAsync(() => activity.Text = message);

    static Uri? WebLink(string text) => Uri.TryCreate(text, UriKind.Absolute, out var uri)
        && uri.Scheme is "http" or "https" ? uri : null;

    void ShowText(string text) => Dispatcher.InvokeAsync(() =>
    {
        messageText.Text = text;
        messagePanel.Visibility = Visibility.Visible;
        openLink.IsEnabled = WebLink(text) is not null;
    });
}
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

internal static class Updates
{
    internal static readonly string[] Files = [
        "iFerry.exe", "iFerry.dll", "iFerry.deps.json", "iFerry.runtimeconfig.json",
        "QRCoder.dll", "Configure-SmbShare.ps1", "New-SmbTransferAccount.ps1", "Update-iFerry.ps1"
    ];
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(2) };
    internal static Version Current => Assembly.GetExecutingAssembly().GetName().Version!;

    static Updates() => Http.DefaultRequestHeaders.UserAgent.ParseAdd("iFerry-Updater/1");

    internal static async Task<(string Stage, string Tag)?> DownloadAsync()
    {
        const string project = "muratkorayozkan/iFerry";
        using var json = JsonDocument.Parse(await Http.GetStringAsync($"https://api.github.com/repos/{project}/releases/latest"));
        var release = json.RootElement;
        string tag = release.GetProperty("tag_name").GetString() ?? "";
        if (!tag.StartsWith('v') || !Version.TryParse(tag[1..], out var version) ||
            version.Build < 0 || version.Revision != -1 || $"{version:3}" != tag[1..] ||
            new Version(version.Major, version.Minor, version.Build, 0) <= Current)
            return null;
        string filename = $"iFerry-{tag}-Windows-x64.zip";
        string url = $"https://github.com/{project}/releases/download/{tag}/{filename}";
        var asset = release.GetProperty("assets").EnumerateArray().Single(a =>
            a.GetProperty("name").GetString() == filename && a.GetProperty("state").GetString() == "uploaded");
        string digest = asset.GetProperty("digest").GetString() ?? "";
        long size = asset.GetProperty("size").GetInt64();
        if (!digest.StartsWith("sha256:", StringComparison.Ordinal) || digest.Length != 71 || size is <= 0 or > 32_000_000)
            throw new InvalidDataException("Güncelleme özeti veya boyutu geçersiz.");
        using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await using var input = await response.Content.ReadAsStreamAsync(timeout.Token);
        using var zip = new MemoryStream();
        byte[] buffer = new byte[65536];
        int read;
        while ((read = await input.ReadAsync(buffer, timeout.Token)) > 0)
        {
            if (zip.Length + read > size) throw new InvalidDataException("Güncelleme boyutu uyuşmuyor.");
            zip.Write(buffer, 0, read);
        }
        if (zip.Length != size || !Convert.ToHexString(SHA256.HashData(zip.GetBuffer().AsSpan(0, (int)size)))
            .Equals(digest[7..], StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Güncelleme özeti uyuşmuyor.");
        zip.Position = 0;
        using var archive = new ZipArchive(zip);
        if (archive.Entries.Count != Files.Length || Files.Any(name => archive.Entries.Count(e => e.FullName == name) != 1) ||
            archive.Entries.Sum(e => e.Length) > 64_000_000)
            throw new InvalidDataException("Güncelleme paketinin içeriği geçersiz.");
        string stage = Path.Combine(Path.GetTempPath(), $"iFerry-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stage);
        try
        {
            foreach (var entry in archive.Entries)
                entry.ExtractToFile(Path.Combine(stage, entry.FullName));
            var packaged = AssemblyName.GetAssemblyName(Path.Combine(stage, "iFerry.dll")).Version;
            if (packaged != new Version(version.Major, version.Minor, version.Build, 0))
                throw new InvalidDataException("Paket sürümü yayın etiketiyle uyuşmuyor.");
            return (stage, tag);
        }
        catch { Directory.Delete(stage, true); throw; }
    }
}

using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;

using var client = new HttpClient();
var directory = Directory.CreateTempSubdirectory();
var folder = directory.FullName;
static void Check(bool result, string message)
{
    if (!result) throw new Exception(message);
}
static async Task Close(WebApplication server)
{
    await server.StopAsync();
    await server.DisposeAsync();
}
try
{
    string? message = null;
    var updates = new System.Collections.Concurrent.ConcurrentQueue<string>();
    var (server, url) = await BrowserTransfer.StartAsync(IPAddress.Loopback, folder, updates.Enqueue, text => message = text);
    const int chunk = 4194304;
    long size = 256L * chunk + 12345;
    using var expected = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    byte[] bytes = new byte[chunk];
    var start = await client.PostAsync($"{url}/start?name=video.bin&size={size}", null);
    start.EnsureSuccessStatusCode();
    var path = url + "/part/" + await start.Content.ReadAsStringAsync();
    for (long offset = 0; offset < size; offset += chunk)
    {
        int n = (int)Math.Min(chunk, size-offset);
        BitConverter.TryWriteBytes(bytes, offset);
        expected.AppendData(bytes, 0, n);
        using var data = new ByteArrayContent(bytes, 0, n);
        using var result = await client.PostAsync($"{path}?offset={offset}", data);
        result.EnsureSuccessStatusCode();
        if (offset == chunk)
        {
            var address = new Uri(path);
            using (var socket = new System.Net.Sockets.TcpClient())
            {
                await socket.ConnectAsync(address.Host, address.Port);
                using var stream = socket.GetStream();
                var headers = $"POST {address.AbsolutePath}?offset={2L*chunk} HTTP/1.1\r\nHost: {address.Authority}\r\nContent-Length: {chunk}\r\n\r\n";
                await stream.WriteAsync(System.Text.Encoding.ASCII.GetBytes(headers));
                await stream.WriteAsync(bytes.AsMemory(0, 65536));
            }
            using var fresh = new HttpClient();
            var state = await fresh.GetStringAsync(path);
            Check(state.Contains("8388608"), "Resume offset");
            using var replay = await fresh.PostAsync($"{path}?offset={offset}", new ByteArrayContent(bytes));
            Check(replay.StatusCode == HttpStatusCode.Conflict, "Duplicate chunk");
        }
    }
    using (var video = File.OpenRead(Path.Combine(folder, "video.bin")))
        Check(expected.GetHashAndReset().SequenceEqual(SHA256.HashData(video)), "Large file content");
    using var textResult = await client.PostAsync(url,new FormUrlEncodedContent(new Dictionary<string,string>{{"text","https://example.com/"}}));
    Check(textResult.IsSuccessStatusCode && message == "https://example.com/", "Shortcut text");
    using var form = new MultipartFormDataContent();
    form.Add(new StringContent("legacy"),"files","old.txt");
    (await client.PostAsync(url, form)).EnsureSuccessStatusCode();
    Check(File.ReadAllText(Path.Combine(folder,"old.txt"))=="legacy", "Legacy Shortcut");
    Check(updates.Any(x => x.Contains("Tamamlandı")), "Upload status");
    await Close(server);

    var paths = new[]{"a.txt","b.txt","c.txt"}.Select(name=>Path.Combine(folder,name)).ToArray();
    foreach(var file in paths) File.WriteAllText(file, Path.GetFileName(file));
    var download = await BrowserTransfer.StartDownloadAsync(IPAddress.Loopback,paths,updates.Enqueue);
    Check((await client.GetStringAsync(download.Address+"/list")).Contains("c.txt"),"List");
    Check(await client.GetStringAsync(download.Address+"/file/2")=="c.txt","Single selection");
    using(var archive = new ZipArchive(new MemoryStream(await client.GetByteArrayAsync(download.Address+"?item=0&item=2"))))
        Check(archive.Entries.Select(e=>e.Name).SequenceEqual(new[]{"a.txt","c.txt"}),"ZIP subset");
    Check((await client.GetAsync(download.Address+"/file/9")).StatusCode==HttpStatusCode.NotFound,"Unknown file");
    await Close(download.Server);

    var key = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
    var first = await BrowserTransfer.StartAsync(IPAddress.Loopback,folder,pairing:key);
    await Close(first.Server);
    var second = await BrowserTransfer.StartAsync(IPAddress.Loopback,folder,pairing:key);
    Check(first.Address == second.Address,"Stable pairing");
    await Close(second.Server);
    var reset = await BrowserTransfer.StartAsync(IPAddress.Loopback,folder,pairing:Convert.ToHexString(RandomNumberGenerator.GetBytes(24)));
    Check((await client.GetAsync(first.Address)).StatusCode==HttpStatusCode.NotFound,"Revoked link");
    await Close(reset.Server);
    Console.WriteLine("PASS: 1 GiB+ upload, resume offset, replay, SHA-256, Shortcut uploads and text, transfer status, selective downloads, pairing rotation.");
}
finally {directory.Delete(true);}

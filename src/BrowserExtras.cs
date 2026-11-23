using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

internal static partial class BrowserTransfer
{
    const int ChunkSize = 4 * 1024 * 1024;

    sealed class Upload(string name, long size, string folder)
    {
        public readonly string Name = name;
        public readonly long Size = size;
        public readonly string Temporary = Path.Combine(folder, $".{Guid.NewGuid():N}.upload");
        public readonly SemaphoreSlim Gate = new(1);
        public long Offset;
        public bool Done;
        public readonly Stopwatch Clock = Stopwatch.StartNew();
    }

    static void ConfigureChunks(WebApplication server, string route, string folder, Action<string>? report)
    {
        var uploads = new ConcurrentDictionary<Guid, Upload>();
        server.Lifetime.ApplicationStopped.Register(() =>
        {
            foreach (var item in uploads.Values)
                try { File.Delete(item.Temporary); } catch (IOException) { }
        });
        server.MapPost(route + "/start", (string name, long size) =>
        {
            if (size < 0 || string.IsNullOrWhiteSpace(name) || name.Length > 200 ||
                name != Path.GetFileName(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                name.TrimEnd(' ', '.') != name) return Results.BadRequest();
            var id = Guid.NewGuid();
            uploads[id] = new Upload(name, size, folder);
            return Results.Text(id.ToString());
        });
        server.MapGet(route + "/part/{id:guid}", (Guid id) => uploads.TryGetValue(id, out var u)
            ? Results.Json(new { offset = Interlocked.Read(ref u.Offset), done = u.Done }) : Results.NotFound());
        server.MapPost(route + "/part/{id:guid}", async (Guid id, long offset, HttpRequest request, CancellationToken ct) =>
        {
            if (!uploads.TryGetValue(id, out var u)) return Results.NotFound();
            var length = request.ContentLength;
            if (length is null or < 0 or > ChunkSize) return Results.BadRequest();
            await u.Gate.WaitAsync(ct);
            try
            {
                if (offset != u.Offset || u.Done) return Results.Conflict();
                if (length > u.Size - offset || (length == 0 && u.Size != 0)) return Results.BadRequest();
                await using (var disk = new FileStream(u.Temporary, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None))
                {
                    disk.SetLength(offset);
                    disk.Position = offset;
                    await request.Body.CopyToAsync(disk, ct);
                    if (disk.Position != offset + length) return Results.BadRequest();
                }
                var end = offset + length.Value;
                if (end == u.Size)
                {
                    CommitFile(u.Temporary, folder, u.Name);
                    u.Done = true;
                }
                Interlocked.Exchange(ref u.Offset, end);
                report?.Invoke($"{u.Name} · {end:N0}/{u.Size:N0} bayt · {end / Math.Max(.001, u.Clock.Elapsed.TotalSeconds) / 1048576:F1} MiB/sn" +
                    (u.Done ? " · Tamamlandı" : ""));
                return Results.Ok();
            }
            finally { u.Gate.Release(); }
        });
    }

    static void CommitFile(string temp, string folder, string name)
    {
        for (int n = 0; ; n++)
        {
            var candidate = n == 0 ? name : $"{Path.GetFileNameWithoutExtension(name)} ({n}){Path.GetExtension(name)}";
            var target = Path.Combine(folder, candidate);
            try { File.Move(temp, target); return; }
            catch (IOException) when (File.Exists(target)) { }
        }
    }

    static string PickerPage(string route, string[] paths)
    {
        var list = paths.Select((path, index) =>
        {
            string name = WebUtility.HtmlEncode(Path.GetFileName(path));
            string image = ImageType(path) is null ? "" : $"<img loading='lazy' width='72' height='72' style='object-fit:cover' src='{route}/preview/{index}' alt=''>";
            return $"<p><label><input type='checkbox' name='item' value='{index}'> {image} {name}</label> <a href='{route}/file/{index}'>İndir</a></p>";
        });
        return $"<!doctype html><meta charset=utf-8><meta name=viewport content='width=device-width,initial-scale=1'><title>iFerry</title><main style='font:18px system-ui;padding:16px'><h1>Dosya seçin</h1><form action='{route}' onsubmit='return !!this.querySelector(&quot;:checked&quot;)'>{string.Concat(list)}<button>Seçilenleri indir</button></form><p><a href='{route}'>Tümünü indir</a></p></main>";
    }

    static string? ImageType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg", ".png" => "image/png", ".webp" => "image/webp", _ => null
    };

    static async Task CopyTracked(Stream input, Stream output, string name, Action<string>? report, CancellationToken ct)
    {
        var clock = Stopwatch.StartNew();
        long bytes = 0, last = -500;
        var buffer = new byte[81920];
        try
        {
            int read;
            while ((read = await input.ReadAsync(buffer, ct)) != 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), ct);
                bytes += read;
                if (clock.ElapsedMilliseconds - last >= 300)
                {
                    report?.Invoke($"{name} · {bytes:N0} bayt · {bytes / Math.Max(.001, clock.Elapsed.TotalSeconds) / 1048576:F1} MiB/sn");
                    last = clock.ElapsedMilliseconds;
                }
            }
            report?.Invoke($"{name} · {bytes:N0} bayt aktarıldı");
        }
        catch { report?.Invoke($"{name} · Kesildi"); throw; }
    }
}

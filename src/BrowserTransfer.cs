using System.IO;
using System.Net;
using System.IO.Compression;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;

internal static partial class BrowserTransfer
{
    const long Limit = 1024L * 1024 * 1024;

    public static async Task<(WebApplication Server, string Address)> StartAsync(IPAddress ip, string folder,
        Action<string>? report = null, Action<string>? receiveText = null, string? pairing = null)
    {
        Directory.CreateDirectory(folder);
        var (server, route) = CreateSession(ip, pairing);
        ConfigureUpload(server, route, folder, report, receiveText);
        ConfigureChunks(server, route, folder, report);
        return await ListenAsync(server, route);
    }

    static (WebApplication, string) CreateSession(IPAddress ip, string? pairing = null)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Listen(ip, pairing is null ? 0 : 47329);
            options.Limits.MaxRequestBodySize = Limit;
        });
        var server = builder.Build();
        var route = "/" + (pairing ?? Convert.ToHexString(RandomNumberGenerator.GetBytes(24)));
        return (server, route);
    }

    static void ConfigureUpload(WebApplication server, string route, string folder, Action<string>? report, Action<string>? receiveText)
    {
        server.MapGet(route, (HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            return Results.Content(Page, "text/html; charset=utf-8");
        });
        server.MapPost(route, async (HttpRequest request, CancellationToken cancellation) =>
        {
            if (!request.HasFormContentType) return Results.BadRequest("Dosya seçin.");
            var form = await request.ReadFormAsync(new FormOptions { MultipartBodyLengthLimit = Limit }, cancellation);
            if (form.TryGetValue("text", out var value) && form.Files.Count == 0)
            {
                var text = value.ToString();
                if (string.IsNullOrWhiteSpace(text) || text.Length > 32768 || receiveText is null) return Results.BadRequest();
                receiveText(text);
                return Results.Text("Metin alındı.");
            }
            if (form.Files.Count == 0) return Results.BadRequest("Dosya seçin.");
            foreach (var file in form.Files)
            {
                var name = string.Concat(Path.GetFileName(file.FileName)
                    .Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim().TrimEnd('.');
                if (name.Length == 0) name = "Dosya";
                var temporary = Path.Combine(folder, $".{Guid.NewGuid():N}.upload");
                try
                {
                    await using (var stream = File.Create(temporary))
                    using (var input = file.OpenReadStream())
                        await CopyTracked(input, stream, name, report, cancellation);
                    CommitFile(temporary, folder, name);
                }
                finally { File.Delete(temporary); }
            }
            return Results.Content("Dosyalar kaydedildi.", "text/plain; charset=utf-8");
        });
    }

    public static Task<(WebApplication Server, string Address)> StartDownloadAsync(IPAddress ip, string[] paths, Action<string>? report = null)
    {
        if (paths.Length == 0) throw new ArgumentException("Dosya seçin.");
        var (server, route) = CreateSession(ip);
        IResult Download(int index, HttpContext context)
        {
            if ((uint)index >= paths.Length || !File.Exists(paths[index])) return Results.NotFound();
            var path = paths[index];
            if (context.Request.Headers.ContainsKey("Range"))
                return Results.File(path, "application/octet-stream", Path.GetFileName(path), enableRangeProcessing: true);
            return Results.Stream(async output =>
            {
                using var input = File.OpenRead(path);
                await CopyTracked(input, output, Path.GetFileName(path), report, context.RequestAborted);
            }, "application/octet-stream", Path.GetFileName(path));
        }
        server.MapGet(route + "/list", () => Results.Content(PickerPage(route, paths), "text/html; charset=utf-8"));
        server.MapGet(route + "/file/{index:int}", (int index, HttpContext context) => Download(index, context));
        server.MapGet(route + "/preview/{index:int}", (int index) =>
            (uint)index < paths.Length && ImageType(paths[index]) is string type && File.Exists(paths[index])
                ? Results.File(paths[index], type) : Results.NotFound());
        server.MapGet(route, (HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            var selected = paths;
            if (context.Request.Query.TryGetValue("item", out var items))
            {
                var indices = new HashSet<int>();
                foreach (var item in items)
                    if (!int.TryParse(item, out var i) || (uint)i >= paths.Length || !indices.Add(i))
                        return Results.BadRequest();
                if (indices.Count == 1) return Download(indices.Single(), context);
                selected = indices.Select(i => paths[i]).ToArray();
            }
            if (selected.Any(path => !File.Exists(path))) return Results.NotFound();
            if (paths.Length == 1) return Download(0, context);
            return Results.Stream(async output =>
            {
                // ZipArchive writes some entry metadata synchronously.
                var bodyControl = context.Features.Get<IHttpBodyControlFeature>();
                if (bodyControl is not null) bodyControl.AllowSynchronousIO = true;
                await using var archive = await ZipArchive.CreateAsync(output, ZipArchiveMode.Create,
                    leaveOpen: true, entryNameEncoding: null, cancellationToken: context.RequestAborted);
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var path in selected)
                {
                    var name = Path.GetFileName(path);
                    for (int suffix = 1; !names.Add(name); suffix++)
                        name = $"{Path.GetFileNameWithoutExtension(path)} ({suffix}){Path.GetExtension(path)}";
                    using var input = File.OpenRead(path);
                    var entry = archive.CreateEntry(name, CompressionLevel.NoCompression);
                    await using var destination = await entry.OpenAsync(context.RequestAborted);
                    await CopyTracked(input, destination, name, report, context.RequestAborted);
                }
            }, "application/zip", "iFerry.zip");
        });
        return ListenAsync(server, route);
    }

    static async Task<(WebApplication Server, string Address)> ListenAsync(WebApplication server, string route)
    {
        try
        {
            await server.StartAsync();
            return (server, server.Urls.Single() + route);
        }
        catch { await server.DisposeAsync(); throw; }
    }

    const string Page = """
        <!doctype html><html lang="tr"><meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <title>Dosya gönder</title>
        <style>
        body{font:17px system-ui;background:#101820;color:#ecf3f4;margin:0;padding:28px}
        main{max-width:32em;margin:auto}form{background:#192630;border-radius:14px;padding:20px}
        input,button{font:inherit;max-width:100%}button{margin-top:18px;padding:10px 18px;border:0;border-radius:8px;background:#91e4ca;color:#122b29}
        </style><main><h1>Dosya gönder</h1>
        <form method="post" enctype="multipart/form-data">
        <label>Fotoğraf veya dosya seçin <input type="file" name="files" multiple required></label><br>
        <button>Windows’a gönder</button></form><p id="status" role="status"></p>
        <form method="post"><label>Metin veya bağlantı <textarea name="text" maxlength="32768" required></textarea></label><button>Metni gönder</button></form>
        <script>
        const form = document.forms[0], picker = form.elements.files, button = form.querySelector('button');
        const status = document.querySelector('#status'), jobs = new Map(), base = location.pathname;
        async function request(url, options) {
          const response = await fetch(base + url, {cache:'no-store', ...options});
          if (!response.ok) throw Error('HTTP ' + response.status);
          return response;
        }
        form.onsubmit = async event => {
          event.preventDefault();
          button.disabled = picker.disabled = true;
          const files = Array.from(picker.files);
          try {
            for (const [index,file] of files.entries()) {
              if (!jobs.has(file)) jobs.set(file, await (await request('/start?' + new URLSearchParams({name:file.name,size:file.size}), {method:'POST'})).text());
              const path = '/part/' + jobs.get(file);
              let {offset,done} = await (await request(path)).json();
              while (!done) {
                status.textContent = (index+1) + '/' + files.length + ' · ' + file.name + ' · ' + offset.toLocaleString() + ' / ' + file.size.toLocaleString() + ' bayt';
                const chunk = file.slice(offset, offset + 4194304);
                await request(path + '?offset=' + offset, {method:'POST',body:chunk});
                offset += chunk.size;
                done = offset === file.size;
              }
            }
            status.textContent = files.length + ' dosya kaydedildi.';
            picker.value = ''; jobs.clear(); button.textContent = 'Windows’a gönder';
          } catch (error) {
            status.textContent = 'Gönderim durdu. Sayfayı açık tutun ve bağlantı gelince devam edin. ' + error.message;
            button.textContent = 'Devam et';
          } finally { button.disabled = picker.disabled = false; }
        };
        </script></main></html>
        """;
}

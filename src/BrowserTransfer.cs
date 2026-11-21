using System.IO;
using System.Net;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;

internal static class BrowserTransfer
{
    const long Limit = 1024L * 1024 * 1024;

    public static async Task<(WebApplication Server, string Address)> StartAsync(IPAddress ip, string folder)
    {
        Directory.CreateDirectory(folder);
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Listen(ip, 0);
            options.Limits.MaxRequestBodySize = Limit;
        });
        var server = builder.Build();
        var route = "/" + Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        server.MapGet(route, (HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            return Results.Content(Page, "text/html; charset=utf-8");
        });
        server.MapPost(route, async (HttpRequest request, CancellationToken cancellation) =>
        {
            if (!request.HasFormContentType) return Results.BadRequest("Dosya seçin.");
            var form = await request.ReadFormAsync(new FormOptions { MultipartBodyLengthLimit = Limit }, cancellation);
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
                        await file.CopyToAsync(stream, cancellation);
                    for (int suffix = 0; ; suffix++)
                    {
                        var candidate = suffix == 0 ? name : $"{Path.GetFileNameWithoutExtension(name)} ({suffix}){Path.GetExtension(name)}";
                        var destination = Path.Combine(folder, candidate);
                        try { File.Move(temporary, destination); break; }
                        catch (IOException) when (File.Exists(destination)) { }
                    }
                }
                finally { File.Delete(temporary); }
            }
            return Results.Content("Dosyalar kaydedildi.", "text/plain; charset=utf-8");
        });
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
        <button>Windows’a gönder</button></form></main></html>
        """;
}

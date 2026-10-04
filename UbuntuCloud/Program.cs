using Microsoft.AspNetCore.Http.Features;
using UbuntuCloud.Components;
using UbuntuCloud.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddSingleton<FileService>();

builder.Services.AddServerSideBlazor()
    .AddHubOptions(options =>
    {
        options.MaximumReceiveMessageSize = null;
    });

builder.Services.Configure<FormOptions>(options =>
{
    options.ValueLengthLimit = int.MaxValue;
    options.MultipartBodyLengthLimit = long.MaxValue;
    options.MultipartHeadersLengthLimit = int.MaxValue;
});

builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.Limits.MaxRequestBodySize = null;
    serverOptions.Limits.MinResponseDataRate = null;
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();

app.MapGet("/download", async (string fileName, string? subPath, FileService fileService, CancellationToken cancellationToken) =>
{
    subPath ??= "";

    string fullPath = fileService.GetFullPath(subPath, fileName);

    if (File.Exists(fullPath))
    {
        var fileStream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 1048576,
            FileOptions.Asynchronous);

        return Results.File(fileStream, "application/octet-stream", fileName, enableRangeProcessing: true);
    }

    if (Directory.Exists(fullPath))
    {
        try
        {
            string archivePath = fileService.CreateFolderZipStream(subPath, fileName, cancellationToken);

            bool isTarGz = archivePath.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase);
            string contentType = isTarGz ? "application/gzip" : "application/zip";
            string downloadFileName = isTarGz ? $"{fileName}.tar.gz" : $"{fileName}.zip";

            var archiveStream = new FileStream(
                archivePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 1048576,
                FileOptions.Asynchronous);

            return Results.File(archiveStream, contentType, downloadFileName, enableRangeProcessing: true);
        }
        catch (OperationCanceledException)
        {
            return Results.StatusCode(499);
        }
    }

    return Results.NotFound("Plik lub folder nie istnieje.");
});

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
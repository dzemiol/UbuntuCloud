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

app.MapGet("/download", (string fileName, string? subPath, FileService fileService) =>
{
    subPath ??= "";

    string fullPath = fileService.GetFullPath(subPath, fileName);

    if (File.Exists(fullPath))
    {
        var fileStream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Results.File(fileStream, "application/octet-stream", fileName);
    }

    if (Directory.Exists(fullPath))
    {
        string tempZipPath = fileService.CreateFolderZipStream(subPath, fileName);

        var zipStream = new FileStream(
            tempZipPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.None,
            4096,
            FileOptions.DeleteOnClose);

        return Results.File(zipStream, "application/zip", $"{fileName}.zip");
    }

    return Results.NotFound("Plik lub folder nie istnieje.");
});

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
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

app.MapGet("/download", async (string fileName, string subPath, FileService fileService) =>
{
    subPath ??= "";
    var physicalPath = fileService.GetPhysicalPath(subPath, fileName);

    if (Directory.Exists(physicalPath))
    {
        var zipBytes = await fileService.GetFolderZipBytesAsync(subPath, fileName);
        return Results.File(zipBytes, "application/zip", $"{fileName}.zip");
    }

    if (File.Exists(physicalPath))
    {
        var fileBytes = await File.ReadAllBytesAsync(physicalPath);
        return Results.File(fileBytes, "application/octet-stream", fileName);
    }

    return Results.NotFound("Plik lub folder nie istnieje.");
});

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
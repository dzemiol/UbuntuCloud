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
    string tempZipPath = fileService.CreateFolderZipStream(subPath ?? "", fileName);

    var fileStream = new FileStream(
        tempZipPath,
        FileMode.Open,
        FileAccess.Read,
        FileShare.None,
        4096,
        FileOptions.DeleteOnClose);

    return Results.File(fileStream, "application/zip", $"{fileName}.zip");
});

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
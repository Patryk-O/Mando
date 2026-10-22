using AnonymousSurvey.Application;
using DevExpress.Blazor;
using AnonymousSurvey.Infrastructure;
using AnonymousSurvey.Infrastructure.Persistence;
using AnonymousSurvey.Web.Components;

var builder = WebApplication.CreateBuilder(args);

var devExpressLicense = builder.Configuration["DevExpress:LicenseKey"];
if (!string.IsNullOrWhiteSpace(devExpressLicense)
    && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DevExpress_License")))
{
    Environment.SetEnvironmentVariable("DevExpress_License", devExpressLicense);
}

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddDevExpressBlazor(options => options.SizeMode = SizeMode.Medium);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

if (app.Configuration.GetValue("Database:InitializeOnStartup", true))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().InitializeAsync();
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

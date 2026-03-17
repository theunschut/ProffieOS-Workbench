using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor.Services;
using ProffieOS.Workbench;
using ProffieOS.Workbench.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddMudServices();

// Saber services as singletons so state persists across page navigations
builder.Services.AddSingleton<SaberCommandService>();
builder.Services.AddSingleton<SaberConnectionService>();
builder.Services.AddSingleton<SaberStateService>();

await builder.Build().RunAsync();

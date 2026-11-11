using Jellyfin.Web;
using Jellyfin.Web.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");
builder.Services.AddJellyfinWeb(builder.HostEnvironment.BaseAddress);

var host = builder.Build();

// Everything the first render depends on (config, device identity, server address, strings, theme)
// is loaded while the splash logo from index.html is still on screen.
await host.Services.GetRequiredService<AppStartup>().InitializeAsync();
await host.RunAsync();

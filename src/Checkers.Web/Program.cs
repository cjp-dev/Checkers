using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.JSInterop;
using Checkers.App.Services;
using Checkers.App.ViewModels;
using Checkers.Web;
using Checkers.Web.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddSingleton(new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddSingleton(services => (IJSInProcessRuntime)services.GetRequiredService<IJSRuntime>());
builder.Services.AddSingleton<BrowserDialogService>();
builder.Services.AddSingleton<IDialogService>(services => services.GetRequiredService<BrowserDialogService>());
builder.Services.AddSingleton<IGameFileService, BrowserGameFileService>();
builder.Services.AddSingleton<ISoundService, BrowserSoundService>();
builder.Services.AddSingleton<BrainDocs>();
builder.Services.AddSingleton<AppearanceStore>();
builder.Services.AddSingleton<ISettingsStore, LocalStorageSettingsStore>();
builder.Services.AddSingleton<IEngineHost, WebEngineHost>();
builder.Services.AddSingleton(services => new MainViewModel(
    session: null,
    services.GetRequiredService<ISoundService>(),
    services.GetRequiredService<IDialogService>(),
    services.GetRequiredService<IGameFileService>(),
    services.GetRequiredService<IEngineHost>(),
    services.GetRequiredService<ISettingsStore>()));

var host = builder.Build();

// Like the other games, the browser version starts with the human playing White against the computer.
await host.Services.GetRequiredService<MainViewModel>().SetGameMode(Checkers.App.Models.GameMode.HumanVsComputer);

await host.RunAsync();

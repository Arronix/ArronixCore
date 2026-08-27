using Arronix.Client.Composition;
using Arronix.Client.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Arronix.Client;

/// <summary>
/// The entry point.
/// </summary>
internal static class Program
{
    private static async Task Main(string[] args)
    {
        var builder = WebAssemblyHostBuilder.CreateDefault(args);

        builder.RootComponents.Add<App>("#app");
        builder.RootComponents.Add<HeadOutlet>("head::after");

        builder.Services.AddArronixClient(
            builder.Configuration,
            new Uri(builder.HostEnvironment.BaseAddress));

        var host = builder.Build();

        // Resolved for its subscription: a tab must stop serving a withdrawn contract when the host says an
        // extension changed state, not when someone happens to press a button. Before the stream opens, so
        // the first such event is not delivered to nothing.
        _ = host.Services.GetRequiredService<ContractStateWatcher>();

        // Opened before the first render so that the shell comes up already connected. A failure here is
        // not fatal and is not awaited to completion: the stream reports it, the connectivity state shows
        // it, and the recovery loop deals with it.
        _ = host.Services.GetRequiredService<EventStream>().StartAsync();

        await host.RunAsync();
    }
}

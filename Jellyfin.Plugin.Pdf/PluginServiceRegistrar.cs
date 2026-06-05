using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Controller.Providers;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.Pdf;

/// <summary>
/// Registers the plugin's services with Jellyfin's dependency injection container.
/// Required in Jellyfin 10.9+ for plugin services to be discoverable.
/// </summary>
public class PluginServiceRegistrar : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<IDynamicImageProvider, PdfImageProvider>();
    }
}

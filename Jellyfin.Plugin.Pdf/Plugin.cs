using System;
using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Plugin.Pdf.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.Pdf;

/// <summary>
/// The PDF thumbnail plugin for Jellyfin.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    /// <summary>
    /// The plugin GUID.
    /// </summary>
    public const string PluginGuid = "a5e3a6c8-2d4b-4f3e-9c1a-7b8d6e5f4a2c";

    /// <summary>
    /// Initializes a new instance of the <see cref="Plugin"/> class.
    /// </summary>
    /// <param name="applicationPaths">Instance of the <see cref="IApplicationPaths"/> interface.</param>
    /// <param name="xmlSerializer">Instance of the <see cref="IXmlSerializer"/> interface.</param>
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    /// <inheritdoc />
    public override string Name => "PDF Thumbnails";

    /// <inheritdoc />
    public override Guid Id => Guid.Parse(PluginGuid);

    /// <inheritdoc />
    public override string Description => "Generates thumbnail images from PDF files for display in Jellyfin.";

    /// <summary>
    /// Gets the current plugin instance.
    /// </summary>
    public static Plugin? Instance { get; private set; }

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        return
        [
            new PluginPageInfo
            {
                Name = Name,
                EmbeddedResourcePath = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}.Configuration.configPage.html",
                    GetType().Namespace)
            }
        ];
    }
}

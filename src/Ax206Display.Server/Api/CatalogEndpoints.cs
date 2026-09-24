using Ax206Display.DataSources.Proxmox;
using Ax206Display.Engine.Catalog;
using Ax206Display.Rendering.Playback;

namespace Ax206Display.Server.Api;

/// <summary>The choices the layout editor offers: widget types, readings, colors, fonts.</summary>
public static class CatalogEndpoints
{
    public static void MapCatalogEndpoints(this IEndpointRouteBuilder api)
    {
        api.MapGet("/catalog", (ProxmoxNodeDirectory nodes, ProxmoxGuestDirectory guests) => Results.Ok(new
        {
            types = WidgetCatalog.Types,
            statKeys = WidgetCatalog.BuildAvailableStatKeys(nodes, guests),
            colors = WidgetCatalog.Colors,
            fonts = WidgetCatalog.FontFamilies.Where(f => f != WidgetCatalog.DefaultFontLabel),
            fontSizes = WidgetCatalog.FontSizes.Where(s => s.Pixels is not null).Select(s => s.Pixels),
            timeFormats = WidgetCatalog.TimeFormats,
        }));

        // A starting widget sized and placed for the given canvas - the same
        // defaults the Windows designer's "Add" buttons use.
        api.MapGet("/catalog/new-widget", (string type, int width, int height, int zOrder) =>
            WidgetCatalog.Types.Any(t => t.Type == type)
                ? Results.Ok(WidgetCatalog.CreateDefault(type, Math.Max(1, width), Math.Max(1, height), zOrder).ToConfig())
                : Results.BadRequest(new { error = $"Unknown widget type '{type}'." }));

        // Current readings, so the editor can show a value next to each data key.
        api.MapGet("/data", (IRenderDataProvider data) => Results.Ok(data.GetSnapshot()));
    }
}

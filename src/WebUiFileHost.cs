using System.Reflection;
using Microsoft.AspNetCore.Http;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Logging;
using SPTarkov.Server.Core.Servers.Http;

namespace Leaves_WebUI;

// The single HTTP surface that serves every web-UI STATIC file for the whole Leaves mod suite - the
// nav rail script, the shared stylesheet, and every mod's page HTML / page CSS / page-side scripts.
// The files used to live in each mod's own assets/ui/ and be served by that mod's IHttpListener; they
// were consolidated here so there is one place to maintain them (see WEBUI_EXTRACTION_PLAN.md).
//
// This mod serves FILES ONLY. Every JSON / compute / POST endpoint each page fetches still lives in
// its own mod (Questrandomizer's /api/*, LeavesUtil's /webui/db/*, GuildStash's /precheck, etc.) - the
// pages hit those on the same origin, so nothing about the data side changed.
//
// Dispatch: SPT asks every listener's CanHandle. Each other mod's CanHandle was narrowed to EXCLUDE the
// exact page paths below, so exactly one listener (this one) claims each - the order listeners are
// asked in doesn't matter. CanHandle here is therefore an exact-path allowlist, NOT a prefix match:
// "/questrandomizer/" as a whole still belongs to Questrandomizer's API listener.
[Injectable]
public class WebUiFileHost(ModHelper modHelper) : IHttpListener, IOnLoad
{
    // One served path. Exactly one of File / RedirectTo is set.
    //   File       - path relative to assets/ui/ (forward slashes), read fresh per request.
    //   RedirectTo - a permanent (301) redirect target instead of a file (legacy-alias URLs).
    private sealed record StaticRoute(
        string Path,
        string? File,
        string ContentType,
        string? RedirectTo
    );

    private const string Html = "text/html; charset=utf-8";
    private const string Css = "text/css; charset=utf-8";
    private const string Json = "application/json; charset=utf-8";
    // Client scripts are stored on disk as .txt (SPT's ModValidator rejects a mod folder that contains
    // a .js/.ts file) and served here at their .js URL - the browser only looks at the Content-Type.
    private const string JavaScript = "application/javascript; charset=utf-8";

    // Every web-UI static file across the suite, keyed by the URL it is served at. The URLs are
    // unchanged from when each mod served the file itself, so no page needed relinking and existing
    // bookmarks keep working.
    private static readonly List<StaticRoute> Routes = new()
    {
        // ---- Suite-wide shared (was Leaves-LeavesUtil/assets/ui) ----
        new StaticRoute("/webui/rail.js",        "shared/rail.txt",           JavaScript, null),
        new StaticRoute("/webui/base.css",       "shared/base.css",           Css,        null),
        new StaticRoute("/webui/db",             "shared/itembrowser.html",   Html,       null),
        new StaticRoute("/webui/config/ui",      "shared/config-editor.html", Html,       null),
        new StaticRoute("/webui/dev/routes",     "shared/dev.html",           Html,       null),
        new StaticRoute("/webui/dev/loadorder",  "shared/loadorder.html",     Html,       null),

        // ---- Quest Randomizer (was Leaves-Questrandomizer/assets/ui) ----
        new StaticRoute("/questrandomizer/ui",                    "questrandomizer/questrandomizer.html", Html,       null),
        new StaticRoute("/questrandomizer/ui/handbook",           "questrandomizer/handbook.html",        Html,       null),
        new StaticRoute("/questrandomizer/ui/info",               "questrandomizer/info.html",            Html,       null),
        new StaticRoute("/questrandomizer/ui/debug",              "questrandomizer/debug.html",           Html,       null),
        new StaticRoute("/questrandomizer/ui/styles.css",         "questrandomizer/styles.css",           Css,        null),
        new StaticRoute("/questrandomizer/ui/nav.json",           "questrandomizer/nav.json",             Json,       null),
        new StaticRoute("/questrandomizer/ui/required-list.js",   "questrandomizer/required-list.txt",    "text/javascript; charset=utf-8", null),
        // Legacy alias: this landing page used to live at /ui/reroll (back when re-rolling was its whole
        // point). It is the mod's main page now - permanently redirect the old path.
        new StaticRoute("/questrandomizer/ui/reroll",             null,                                   Html,       "/questrandomizer/ui"),

        // ---- Barter Economy (was Leaves-BarterEconomy/assets/ui) ----
        new StaticRoute("/bartereconomy/ui",           "bartereconomy/bartereconomy.html", Html,       null),
        new StaticRoute("/bartereconomy/ui/tiers",     "bartereconomy/tiers.html",         Html,       null),
        new StaticRoute("/bartereconomy/ui/trades",    "bartereconomy/trades.html",        Html,       null),
        new StaticRoute("/bartereconomy/ui/search",    "bartereconomy/search.html",        Html,       null),
        new StaticRoute("/bartereconomy/ui/pinned",    "bartereconomy/pinned.html",        Html,       null),
        new StaticRoute("/bartereconomy/ui/common.js", "bartereconomy/common.txt",         JavaScript, null),
        new StaticRoute("/bartereconomy/ui/nav.json",  "bartereconomy/nav.json",           Json,       null),

        // ---- Loot Fuckery (was Leaves-LootFuckery/assets/ui) ----
        // NOTE: /lootfuckery/api/maps and /lootfuckery/maps/* stay in Leaves-LootFuckery - they serve
        // the DynamicMaps SVG/jsonc data under assets/maps, which is mod data, not a UI file.
        new StaticRoute("/lootfuckery/ui/dump",    "lootfuckery/dump.html",    Html, null),
        new StaticRoute("/lootfuckery/ui/history", "lootfuckery/history.html", Html, null),

        // ---- Guild Stash (was Leaves-GuildStash-Server/assets/ui) ----
        new StaticRoute("/guildstash/ui", "guildstash/guildstash.html", Html, null),
    };

    // Path -> route, built once from Routes. Keys are lower-cased and have no trailing slash; lookups
    // normalise the request path the same way, so "/webui/db/" and "/WebUI/DB" both resolve.
    private static readonly Dictionary<string, StaticRoute> RoutesByPath = BuildRouteIndex();

    private static Dictionary<string, StaticRoute> BuildRouteIndex()
    {
        var index = new Dictionary<string, StaticRoute>(StringComparer.OrdinalIgnoreCase);
        foreach (StaticRoute route in Routes)
        {
            index[Normalise(route.Path)] = route;
        }
        return index;
    }

    private static string Normalise(string path)
    {
        string trimmed = path.TrimEnd('/');
        return trimmed.Length == 0 ? "/" : trimmed;
    }

    // Publish the served pages into the suite-wide route directory (the Dev page at /webui/dev/routes
    // reads it back). Utils.Instance is set well before any OnLoad.
    public Task OnLoad()
    {
        var routeInfos = new List<Leaves.RouteInfo>();
        foreach (StaticRoute route in Routes)
        {
            string description;
            string kind;
            if (route.RedirectTo != null)
            {
                description = "Legacy alias -> permanent redirect to " + route.RedirectTo + ".";
                kind = "internal";
            }
            else if (route.ContentType == Html)
            {
                description = "Web page (static file " + route.File + ").";
                kind = "ui";
            }
            else
            {
                description = "Web-UI asset (static file " + route.File + ").";
                kind = "internal";
            }
            routeInfos.Add(new Leaves.RouteInfo("GET", route.Path, description, kind));
        }
        Leaves.Utils.Instance.RegisterRoutes("Web UI (static files)", routeInfos);
        return Task.CompletedTask;
    }

    // assets/ui/ under this mod's own folder. Files are read fresh per request (these pages are hit
    // rarely) so a tweak to a deployed file shows up without a rebuild.
    private readonly string uiDir = System.IO.Path.Combine(
        modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly()),
        "assets", "ui");

    public bool CanHandle(MongoId sessionId, HttpContext context)
    {
        if (context.Request.Method != "GET")
        {
            return false;
        }
        string path = Normalise(context.Request.Path.Value ?? "");
        return RoutesByPath.ContainsKey(path);
    }

    public async Task Handle(MongoId sessionId, HttpContext context)
    {
        string rawPath = context.Request.Path.Value ?? "";
        string path = Normalise(rawPath);

        try
        {
            if (!RoutesByPath.TryGetValue(path, out StaticRoute? route))
            {
                // CanHandle already gated this, so we only get here on a genuine mismatch.
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                context.Response.ContentType = "text/plain; charset=utf-8";
                await context.Response.WriteAsync("Unknown Web UI resource: " + rawPath);
                return;
            }

            if (route.RedirectTo != null)
            {
                context.Response.Redirect(route.RedirectTo, permanent: true);
                return;
            }

            string fullPath = System.IO.Path.Combine(uiDir, route.File!.Replace('/', System.IO.Path.DirectorySeparatorChar));
            await Leaves.HttpUtils.ServeFile(context, fullPath, route.ContentType);
        }
        catch (Exception ex)
        {
            Leaves.Utils.Instance.printColor($"[WebUI] Error serving {rawPath}: {ex.Message}", LogTextColor.Red);
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "text/plain; charset=utf-8";
            await context.Response.WriteAsync("Error serving " + rawPath + ": " + ex.Message);
        }
    }
}

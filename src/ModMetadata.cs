using System.Collections.Generic;
using System.Reflection;
using SPTarkov.Server.Core.Models.Spt.Mod;

namespace Leaves_WebUI;

// Leaves-WebUI has no gameplay behaviour: it is the single place the whole mod suite's web-UI static
// files (HTML / CSS / client scripts) live and are served from. Each other mod keeps serving its own
// JSON / compute / POST endpoints; this mod only does "read the file, return it with a content type".
// This record exists so SPT's mod loader treats the folder as a real mod and registers its
// [Injectable] WebUiFileHost.
public record ModMetadata : AbstractModMetadata
{
    public override string ModGuid { get; init; } = "Leaves.You.Alone.WebUI";
    public override string Name { get; init; } = "LeavesWebUI";
    public override string Author { get; init; } = "DeadLeaves";
    public override List<string>? Contributors { get; init; } = new List<string> { "Leaves" };
    public override SemanticVersioning.Version Version { get; init; } = new(GetAssemblyVersion());
    public override SemanticVersioning.Range SptVersion { get; init; } = new("~4.0.0");
    public override List<string>? Incompatibilities { get; init; }
    public override Dictionary<string, SemanticVersioning.Range>? ModDependencies { get; init; } = new Dictionary<string, SemanticVersioning.Range>
    {
        { "Leaves.You.Alone.LeavesUtils", new SemanticVersioning.Range("^1.0.0") }
    };
    public override string? Url { get; init; } = "https://github.com/DeadLeavez/Leaves-WebUI";
    public override bool? IsBundleMod { get; init; } = false;
    public override string License { get; init; } = "MIT";

    private static string GetAssemblyVersion()
    {
        return Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion ?? "1.0.0";
    }
}

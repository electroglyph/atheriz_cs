// Port of atheriz/new.py:292 generate_settings_template
// Port of atheriz/settings.py defaults
namespace MyGame;


/// <summary>
/// Game settings — mirrors generated <c>settings.py</c>.
/// Override defaults from <see cref="AtherizSettings"/> here.
/// This template intentionally does NOT mutate engine via reflection at startup;
/// register entity replacements via <c>[EntityReplacement]</c> and <see cref="Atheriz.Core.Plugins.PluginLoader"/>.
/// </summary>
/// <remarks>
/// Template source for <c>atheriz new</c> — scaffolded into new game folders by
/// <c>GameTemplateGenerator</c> as the generated game's settings file.
/// </remarks>
public static class GameSettings
{
    // Paths — mirrors SAVE_PATH="save" / SECRET_PATH="secret"
    public const string SavePath = "save";
    public const string SecretPath = "secret";

    // Display — mirrors SERVERNAME="AtheriZ"
    public const string ServerName = "MyGame";

    // Port of atheriz/settings.py:69 WEBCLIENT_SYNC_CHECK = True
    // Mirrors copy_web_folder — webclient now included, so keep sync check on.
    public const bool WebclientSyncCheck = true;

    // CLASS_INJECTIONS equivalent — C# uses [EntityReplacement] attributes.
    // Python: ("local_module","ClassName","target_import_path") tuple list at new.py:312-323
    // C#: decorate your custom type:
    //   [EntityReplacement(typeof(Atheriz.Core.Objects.GameObject), typeof(CustomObject))]
    //   [EntityReplacement(typeof(Atheriz.Core.Objects.Account), typeof(CustomAccount))]
    //   [EntityReplacement(typeof(Atheriz.Core.Objects.Channel), typeof(CustomChannel))]
    //   [EntityReplacement(typeof(Atheriz.Core.Objects.Node), typeof(CustomNode))]
    //   [EntityReplacement(typeof(Atheriz.Core.Objects.Script), typeof(CustomScript))]
    // PluginLoader scans for these at Load() — mirrors atheriz/atheriz.py:155 setup_game_folder + reloader.py:216 injections.
    // Do NOT call reflection at static startup; discovery is lazy-collectible via AssemblyLoadContext.

    // Example: point to AtherizSettings for overrides
    // See Atheriz.Core.Settings.AtherizSettings for full list (mirrors atheriz/settings.py:317).
}

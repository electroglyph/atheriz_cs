// Port of atheriz/commands/loggedin/create.py:53
using Atheriz.Core.Objects;
using Atheriz.Core.Globals;
using Atheriz.Core.Commands;

namespace Atheriz.Core.Commands.LoggedIn;

public sealed class CreateCommand : Command
{
    public override string Key => "create";
    public override string Desc => "Create a new object.";
    public override string Category => "Building";
    public override bool Access(IMessageTarget caller) => CommandPermissions.IsBuilder(caller);
    protected override void SetupParser(GameArgumentParser p)
    {
        p.AddArgument("name").Help("name of the object to create");
        p.AddArgument("-p", "--is_pc").Help("create as player character").Action(GameArgumentParser.ArgAction.StoreTrue);
        p.AddArgument("-i", "--is_item").Help("create as item").Action(GameArgumentParser.ArgAction.StoreTrue);
        p.AddArgument("-n", "--is_npc").Help("create as NPC").Action(GameArgumentParser.ArgAction.StoreTrue);
        p.AddArgument("-m", "--is_mapable").Help("make object mapable").Action(GameArgumentParser.ArgAction.StoreTrue);
        p.AddArgument("-c", "--is_container").Help("make object a container").Action(GameArgumentParser.ArgAction.StoreTrue);
        p.AddArgument("-t", "--is_tickable").Help("make object tickable").Action(GameArgumentParser.ArgAction.StoreTrue);
        p.AddArgument("desc").Help("description of the object to create").Nargs("REMAINDER");
    }
    public override void Run(IMessageTarget caller, object? args)
    {
        if (caller is not GameObject go) { caller.Msg("You can't do that."); return; }
        var pa = args as GameArgumentParser.ParsedArgs;
        if (pa == null) { go.Msg(PrintHelp()); return; }
        var name = pa.GetString("name");
        if (string.IsNullOrWhiteSpace(name)) { go.Msg(PrintHelp()); return; }
        var descList = pa.GetList("desc");
        var desc = string.Join(" ", descList);
        var obj = GameObject.Create(name!, desc, isPc: pa.GetBool("is_pc"), isItem: pa.GetBool("is_item"), isNpc: pa.GetBool("is_npc"), isMapable: pa.GetBool("is_mapable"), isContainer: pa.GetBool("is_container"), isTickable: pa.GetBool("is_tickable"));
        ObjectRegistry.AddObject(obj);
        obj.MoveTo(go);
        go.Msg($"Created object '{obj.Name}' (ID: {obj.Id}).");
    }
}
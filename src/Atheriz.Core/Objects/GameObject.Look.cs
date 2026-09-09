using Atheriz.Core.Persistence.Dto;

namespace Atheriz.Core.Objects;

// Partial for look/examine-command support — split from GameObject.Move.cs per file-organization hygiene.
public partial class GameObject
{
    // Port of base_obj.py:862 execute_cmd — delegates to CommandDispatcher.DispatchLoggedIn with puppet check (mirrors base_obj.execute_cmd)
    public void ExecuteCommand(string raw, Session? session = null)
    {
        if (string.IsNullOrEmpty(raw)) return; // Port of base_obj.py:874 if not raw_string: return
        // Port of base_obj.py:876-878 from atheriz.inputfuncs import dispatch_loggedin; dispatch_loggedin(self, raw_string)
        // In C# we use Commands.CommandDispatcher
        // session param ignored for compatibility; this object's own session is used for message routing (but we just dispatch)
        try { Commands.CommandDispatcher.DispatchLoggedIn(this, raw); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed GameObject.ExecuteCommand: " + logEx.Message, "GameObject"); }
    }

    // Port of base_obj.py:2073 at_look
    public virtual string AtLook(GameObject? target)
    {
        return Hookable("at_look", () =>
        {
            if (target is null) return "You see nothing here."; // Port of base_obj.py:2085
            if (!target.Access(this, "view")) return $"You can't look at '{target.GetDisplayName(this)}'."; // Port of base_obj.py:2087
            string desc;
            if (target is Node node) desc = node.ReturnAppearance(this);
            else desc = target.ReturnAppearance(this);
            try { target.AtDesc(this); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed GameObject.AtLook: " + logEx.Message, "GameObject"); } // Port of base_obj.py:2090 target.at_desc
            return desc;
        }, target);
    }

    public virtual string ReturnAppearance(GameObject? looker)
    {
        return Hookable("return_appearance", () =>
        {
            if (looker is null) return "";
            // Simplified appearance: name + desc + things
            var name = GetDisplayName(looker);
            var desc = Desc;
            var things = GetDisplayThings(looker);
            // Use appearance_template = "{name}: {desc}{things}" from base_obj.py:78
            return $"{name}: {desc}{things}".Trim();
        }, looker);
    }

    public virtual string GetDisplayThings(GameObject? looker)
    {
        var contents = ObjectRegistry.Get(ContentsSnapshot.ToList());
        var visible = contents.Where(c => c.Access(looker, "view")).ToList();
        if (IsContainer && visible.Count > 0)
        {
            var grouped = ContentUtils.GroupByName(visible, looker);
            return "\n\nInside you see: " + grouped;
        }
        return "";
    }
}

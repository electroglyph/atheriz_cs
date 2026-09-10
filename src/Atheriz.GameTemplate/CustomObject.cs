// Port of atheriz/new.py:522 TEMPLATE_CONFIGS ("object","Object","atheriz.objects.base_obj")
// Dynamically generated via get_class_hooks (atheriz/utils.py:701) — mirrors test/object.py full hook list
#nullable enable
namespace MyGame;
/// <summary>Custom Object — mirrors test/object.py. Override methods below to customize behavior.</summary>
public class CustomObject : GameObject
{
    public CustomObject() : base() { }
    public CustomObject(string name, bool isPc = false) : base() { Name = name; IsPc = isPc; }
}

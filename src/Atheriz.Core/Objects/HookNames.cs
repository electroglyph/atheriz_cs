namespace Atheriz.Core.Objects;

// Canonical hook names dispatched by GameObject.Hookable. Script methods
// with a matching name (marked Before/After/Replace) attach as hooks, so
// dispatch sites reference these constants instead of string literals —
// a typo becomes a compile error instead of a silently skipped hook.
// Values are the exact runtime hook names; renaming a value re-keys hooks.
public static class HookNames
{
    public const string AtAlarm = "at_alarm";
    public const string AtCreate = "at_create";
    public const string AtDelete = "at_delete";
    public const string AtDesc = "at_desc";
    public const string AtDisconnect = "at_disconnect";
    public const string AtDrop = "at_drop";
    public const string AtEmitSound = "at_emit_sound";
    public const string AtGet = "at_get";
    public const string AtGive = "at_give";
    public const string AtHear = "at_hear";
    public const string AtInit = "at_init";
    public const string AtInstall = "at_install";
    public const string AtLegendUpdate = "at_legend_update";
    public const string AtLook = "at_look";
    public const string AtLunarEvent = "at_lunar_event";
    public const string AtMapUpdate = "at_map_update";
    public const string AtMsgReceive = "at_msg_receive";
    public const string AtMsgSend = "at_msg_send";
    public const string AtObjectLeave = "at_object_leave";
    public const string AtObjectReceive = "at_object_receive";
    public const string AtPostMove = "at_post_move";
    public const string AtPostPuppet = "at_post_puppet";
    public const string AtPreDrop = "at_pre_drop";
    public const string AtPreEmitSound = "at_pre_emit_sound";
    public const string AtPreGet = "at_pre_get";
    public const string AtPreGive = "at_pre_give";
    public const string AtPreHear = "at_pre_hear";
    public const string AtPreMapRender = "at_pre_map_render";
    public const string AtPreMove = "at_pre_move";
    public const string AtPreObjectLeave = "at_pre_object_leave";
    public const string AtPreObjectReceive = "at_pre_object_receive";
    public const string AtPrePut = "at_pre_put";
    public const string AtPrePuppet = "at_pre_puppet";
    public const string AtPreSay = "at_pre_say";
    public const string AtPuppet = "at_puppet";
    public const string AtPut = "at_put";
    public const string AtSay = "at_say";
    public const string AtSolarEvent = "at_solar_event";
    public const string AtTick = "at_tick";
    public const string AtUnpuppet = "at_unpuppet";
    public const string ReturnAppearance = "return_appearance";
}

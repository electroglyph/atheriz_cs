namespace Atheriz.Core.Objects;

// Canonical hook names dispatched by GameObject.Hookable. Script methods
// with a matching name (marked Before/After/Replace) attach as hooks, so
// dispatch sites reference these constants instead of string literals —
// a typo becomes a compile error instead of a silently skipped hook.
// Values are the exact runtime hook names; renaming a value re-keys hooks.
// Each constant also carries its dispatch arg-count shapes ([HookArgs]):
// the single source Atheriz.HookGen compiles into the HookName enum map.
public static class HookNames
{
    [HookArgs(2)] public const string AtAlarm = "at_alarm";
    [HookArgs(0)] public const string AtCreate = "at_create";
    [HookArgs(1)] public const string AtDelete = "at_delete";
    [HookArgs(1)] public const string AtDesc = "at_desc";
    [HookArgs(0)] public const string AtDisconnect = "at_disconnect";
    [HookArgs(1)] public const string AtDrop = "at_drop";
    [HookArgs(4)] public const string AtEmitSound = "at_emit_sound";
    [HookArgs(1)] public const string AtGet = "at_get";
    [HookArgs(2)] public const string AtGive = "at_give";
    [HookArgs(5)] public const string AtHear = "at_hear";
    [HookArgs(0)] public const string AtInit = "at_init";
    [HookArgs(0)] public const string AtInstall = "at_install";
    [HookArgs(3)] public const string AtLegendUpdate = "at_legend_update";
    [HookArgs(1)] public const string AtLook = "at_look";
    [HookArgs(1)] public const string AtLunarEvent = "at_lunar_event";
    [HookArgs(6)] public const string AtMapUpdate = "at_map_update";
    [HookArgs(3)] public const string AtMsgReceive = "at_msg_receive";
    [HookArgs(3)] public const string AtMsgSend = "at_msg_send";
    [HookArgs(2)] public const string AtObjectLeave = "at_object_leave";
    [HookArgs(2)] public const string AtObjectReceive = "at_object_receive";
    [HookArgs(2)] public const string AtPostMove = "at_post_move";
    [HookArgs(0)] public const string AtPostPuppet = "at_post_puppet";
    [HookArgs(1)] public const string AtPreDrop = "at_pre_drop";
    [HookArgs(5)] public const string AtPreEmitSound = "at_pre_emit_sound";
    [HookArgs(1)] public const string AtPreGet = "at_pre_get";
    [HookArgs(2)] public const string AtPreGive = "at_pre_give";
    [HookArgs(5)] public const string AtPreHear = "at_pre_hear";
    [HookArgs(1)] public const string AtPreMapRender = "at_pre_map_render";
    [HookArgs(2)] public const string AtPreMove = "at_pre_move";
    [HookArgs(2)] public const string AtPreObjectLeave = "at_pre_object_leave";
    [HookArgs(2)] public const string AtPreObjectReceive = "at_pre_object_receive";
    [HookArgs(2)] public const string AtPrePut = "at_pre_put";
    [HookArgs(1)] public const string AtPrePuppet = "at_pre_puppet";
    [HookArgs(1)] public const string AtPreSay = "at_pre_say";
    [HookArgs(1)] public const string AtPuppet = "at_puppet";
    [HookArgs(2)] public const string AtPut = "at_put";
    [HookArgs(2, 8)] public const string AtSay = "at_say";
    [HookArgs(1)] public const string AtSolarEvent = "at_solar_event";
    [HookArgs(0)] public const string AtTick = "at_tick";
    [HookArgs(1)] public const string AtUnpuppet = "at_unpuppet";
    [HookArgs(1)] public const string ReturnAppearance = "return_appearance";
    [HookArgs(0, 1)] public const string AtServerStart = "at_server_start";
    [HookArgs(0, 1)] public const string AtServerStop = "at_server_stop";
    [HookArgs(0, 1)] public const string AtServerReload = "at_server_reload";
}

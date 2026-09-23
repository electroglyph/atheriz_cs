namespace Atheriz.Core.Objects;

// Typed hook dispatch: every engine hook has an enum member whose Name() is
// the exact runtime hook string (the HookNames constant). Dispatch and attach
// sites take HookName, so a typo is a compile error instead of a silently
// skipped hook; the string overloads stay for custom (game-defined) hooks.
// Attach-time arity validation (InstallHook) uses DispatchArities: the exact
// argument counts each hook's dispatch sites pass today.
public enum HookName
{
    AtAlarm,
    AtCreate,
    AtDelete,
    AtDesc,
    AtDisconnect,
    AtDrop,
    AtEmitSound,
    AtGet,
    AtGive,
    AtHear,
    AtInit,
    AtInstall,
    AtLegendUpdate,
    AtLook,
    AtLunarEvent,
    AtMapUpdate,
    AtMsgReceive,
    AtMsgSend,
    AtObjectLeave,
    AtObjectReceive,
    AtPostMove,
    AtPostPuppet,
    AtPreDrop,
    AtPreEmitSound,
    AtPreGet,
    AtPreGive,
    AtPreHear,
    AtPreMapRender,
    AtPreMove,
    AtPreObjectLeave,
    AtPreObjectReceive,
    AtPrePut,
    AtPrePuppet,
    AtPreSay,
    AtPuppet,
    AtPut,
    AtSay,
    AtSolarEvent,
    AtTick,
    AtUnpuppet,
    ReturnAppearance,
}

internal static class HookNameExtensions
{
    internal static string Name(this HookName name) => name switch
    {
        HookName.AtAlarm => HookNames.AtAlarm,
        HookName.AtCreate => HookNames.AtCreate,
        HookName.AtDelete => HookNames.AtDelete,
        HookName.AtDesc => HookNames.AtDesc,
        HookName.AtDisconnect => HookNames.AtDisconnect,
        HookName.AtDrop => HookNames.AtDrop,
        HookName.AtEmitSound => HookNames.AtEmitSound,
        HookName.AtGet => HookNames.AtGet,
        HookName.AtGive => HookNames.AtGive,
        HookName.AtHear => HookNames.AtHear,
        HookName.AtInit => HookNames.AtInit,
        HookName.AtInstall => HookNames.AtInstall,
        HookName.AtLegendUpdate => HookNames.AtLegendUpdate,
        HookName.AtLook => HookNames.AtLook,
        HookName.AtLunarEvent => HookNames.AtLunarEvent,
        HookName.AtMapUpdate => HookNames.AtMapUpdate,
        HookName.AtMsgReceive => HookNames.AtMsgReceive,
        HookName.AtMsgSend => HookNames.AtMsgSend,
        HookName.AtObjectLeave => HookNames.AtObjectLeave,
        HookName.AtObjectReceive => HookNames.AtObjectReceive,
        HookName.AtPostMove => HookNames.AtPostMove,
        HookName.AtPostPuppet => HookNames.AtPostPuppet,
        HookName.AtPreDrop => HookNames.AtPreDrop,
        HookName.AtPreEmitSound => HookNames.AtPreEmitSound,
        HookName.AtPreGet => HookNames.AtPreGet,
        HookName.AtPreGive => HookNames.AtPreGive,
        HookName.AtPreHear => HookNames.AtPreHear,
        HookName.AtPreMapRender => HookNames.AtPreMapRender,
        HookName.AtPreMove => HookNames.AtPreMove,
        HookName.AtPreObjectLeave => HookNames.AtPreObjectLeave,
        HookName.AtPreObjectReceive => HookNames.AtPreObjectReceive,
        HookName.AtPrePut => HookNames.AtPrePut,
        HookName.AtPrePuppet => HookNames.AtPrePuppet,
        HookName.AtPreSay => HookNames.AtPreSay,
        HookName.AtPuppet => HookNames.AtPuppet,
        HookName.AtPut => HookNames.AtPut,
        HookName.AtSay => HookNames.AtSay,
        HookName.AtSolarEvent => HookNames.AtSolarEvent,
        HookName.AtTick => HookNames.AtTick,
        HookName.AtUnpuppet => HookNames.AtUnpuppet,
        HookName.ReturnAppearance => HookNames.ReturnAppearance,
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, null),
    };

    internal static HookName? TryParseName(string? funcName) => funcName switch
    {
        HookNames.AtAlarm => HookName.AtAlarm,
        HookNames.AtCreate => HookName.AtCreate,
        HookNames.AtDelete => HookName.AtDelete,
        HookNames.AtDesc => HookName.AtDesc,
        HookNames.AtDisconnect => HookName.AtDisconnect,
        HookNames.AtDrop => HookName.AtDrop,
        HookNames.AtEmitSound => HookName.AtEmitSound,
        HookNames.AtGet => HookName.AtGet,
        HookNames.AtGive => HookName.AtGive,
        HookNames.AtHear => HookName.AtHear,
        HookNames.AtInit => HookName.AtInit,
        HookNames.AtInstall => HookName.AtInstall,
        HookNames.AtLegendUpdate => HookName.AtLegendUpdate,
        HookNames.AtLook => HookName.AtLook,
        HookNames.AtLunarEvent => HookName.AtLunarEvent,
        HookNames.AtMapUpdate => HookName.AtMapUpdate,
        HookNames.AtMsgReceive => HookName.AtMsgReceive,
        HookNames.AtMsgSend => HookName.AtMsgSend,
        HookNames.AtObjectLeave => HookName.AtObjectLeave,
        HookNames.AtObjectReceive => HookName.AtObjectReceive,
        HookNames.AtPostMove => HookName.AtPostMove,
        HookNames.AtPostPuppet => HookName.AtPostPuppet,
        HookNames.AtPreDrop => HookName.AtPreDrop,
        HookNames.AtPreEmitSound => HookName.AtPreEmitSound,
        HookNames.AtPreGet => HookName.AtPreGet,
        HookNames.AtPreGive => HookName.AtPreGive,
        HookNames.AtPreHear => HookName.AtPreHear,
        HookNames.AtPreMapRender => HookName.AtPreMapRender,
        HookNames.AtPreMove => HookName.AtPreMove,
        HookNames.AtPreObjectLeave => HookName.AtPreObjectLeave,
        HookNames.AtPreObjectReceive => HookName.AtPreObjectReceive,
        HookNames.AtPrePut => HookName.AtPrePut,
        HookNames.AtPrePuppet => HookName.AtPrePuppet,
        HookNames.AtPreSay => HookName.AtPreSay,
        HookNames.AtPuppet => HookName.AtPuppet,
        HookNames.AtPut => HookName.AtPut,
        HookNames.AtSay => HookName.AtSay,
        HookNames.AtSolarEvent => HookName.AtSolarEvent,
        HookNames.AtTick => HookName.AtTick,
        HookNames.AtUnpuppet => HookName.AtUnpuppet,
        HookNames.ReturnAppearance => HookName.ReturnAppearance,
        _ => null,
    };

    // Exact dispatch argument counts per hook, read off the Hookable call
    // sites. AtSay has two shapes (AtSay 2, AtSayFull 8); every other hook
    // dispatches one shape everywhere.
    internal static IReadOnlyList<int> DispatchArities(this HookName name) => name switch
    {
        HookName.AtSay => [2, 8],
        HookName.AtAlarm => [2],
        HookName.AtPrePut => [2],
        HookName.AtPut => [2],
        HookName.AtPreGive => [2],
        HookName.AtGive => [2],
        HookName.AtPreMove => [2],
        HookName.AtPostMove => [2],
        HookName.AtPreObjectLeave => [2],
        HookName.AtObjectLeave => [2],
        HookName.AtPreObjectReceive => [2],
        HookName.AtObjectReceive => [2],
        HookName.AtMsgReceive => [3],
        HookName.AtMsgSend => [3],
        HookName.AtLegendUpdate => [3],
        HookName.AtEmitSound => [4],
        HookName.AtHear => [5],
        HookName.AtPreEmitSound => [6],
        HookName.AtPreHear => [6],
        HookName.AtMapUpdate => [6],
        HookName.AtDesc => [1],
        HookName.AtPreSay => [1],
        HookName.AtPuppet => [1],
        HookName.AtUnpuppet => [1],
        HookName.AtDelete => [1],
        HookName.AtSolarEvent => [1],
        HookName.AtLunarEvent => [1],
        HookName.AtLook => [1],
        HookName.ReturnAppearance => [1],
        HookName.AtPreDrop => [1],
        HookName.AtPreGet => [1],
        HookName.AtDrop => [1],
        HookName.AtGet => [1],
        HookName.AtPrePuppet => [1],
        HookName.AtPreMapRender => [1],
        HookName.AtCreate => [0],
        HookName.AtDisconnect => [0],
        HookName.AtInit => [0],
        HookName.AtInstall => [0],
        HookName.AtPostPuppet => [0],
        HookName.AtTick => [0],
        _ => [],
    };

    // Attach-time arity check mirroring DelegateInvoker: a delegate can take
    // a call of L args exactly when RequiredCount <= L <= Types.Length
    // (required counts stop at the first optional/defaulted parameter, same
    // rule as the invoker). Before/replace hooks run with the dispatch args;
    // after hooks also run with args+result, and unmarked delegates can land
    // in either list, so those accept one extra parameter.
    internal static bool AcceptsArity(this HookName name, Delegate hook, HookKind kind)
    {
        var ps = hook.Method.GetParameters();
        int required = 0;
        foreach (var p in ps)
        {
            if (p.IsOptional || p.HasDefaultValue) break;
            required++;
        }
        int total = ps.Length;
        bool afterStyle = kind == HookKind.None || (kind & HookKind.After) != 0;
        foreach (var n in name.DispatchArities())
        {
            if (required <= n && n <= total) return true;
            if (afterStyle && required <= n + 1 && n + 1 <= total) return true;
        }
        return false;
    }
}

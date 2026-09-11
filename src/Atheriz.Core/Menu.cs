namespace Atheriz.Core;
// Port of atheriz/menu.py:15
public sealed class MenuContext{public object? Caller{get;}public Dictionary<string,object?> State{get;}=new();public MenuContext(object? c){Caller=c;}} // Port of menu.py:16-17
// Port of atheriz/menu.py:21
public sealed class Choice{
 public string Key{get;}public string Desc{get;}
 public Func<MenuContext,(string,List<Choice>)>? GotoSync{get;}
 public Func<MenuContext,Task<(string,List<Choice>)>>? GotoAsync{get;}
 public Action<MenuContext>? CallbackSync{get;}public Func<MenuContext,Task>? CallbackAsync{get;}public bool Stay{get;}
 public Choice(string k,string d,Func<MenuContext,(string,List<Choice>)>? gs=null,Func<MenuContext,Task<(string,List<Choice>)>>? ga=null,Action<MenuContext>? cb=null,Func<MenuContext,Task>? cba=null,bool stay=false){Key=k;Desc=d;GotoSync=gs;GotoAsync=ga;CallbackSync=cb;CallbackAsync=cba;Stay=stay;}
}
// Port of atheriz/menu.py:30
public sealed class MenuEngine{
 public MenuContext Context{get;}
 public Func<MenuContext,(string,List<Choice>)>? CurrentNodeSync{get;private set;}
 public Func<MenuContext,Task<(string,List<Choice>)>>? CurrentNodeAsync{get;private set;}
 string _text="";Dictionary<string,Choice> _choices=new(StringComparer.OrdinalIgnoreCase); // Port of menu.py:34-35
 public MenuEngine(object? caller,Func<MenuContext,(string,List<Choice>)> start){Context=new(caller);CurrentNodeSync=start;if(start is not null)_Render();} // Port of menu.py:36
 public MenuEngine(object? caller,Func<MenuContext,Task<(string,List<Choice>)>> startA){Context=new(caller);CurrentNodeAsync=startA;}
  void _Render(){ // Port of menu.py:39
   if(CurrentNodeSync is null&&CurrentNodeAsync is null)return;
   if(CurrentNodeAsync is not null)throw new InvalidOperationException("async menu node requires async render"); // Port of menu.py:42-43
   var (t,cl)=CurrentNodeSync!(Context);_text=t;_choices=BuildChoices(cl); // Port of menu.py:44
  }
  // Shared choice-dict build for the sync/async render paths: case-insensitive
  // map + identical ToLowerInvariant().Trim() duplicate-key throw (menu.py:47-51).
  static Dictionary<string,Choice> BuildChoices(List<Choice> cl){var d=new Dictionary<string,Choice>(StringComparer.OrdinalIgnoreCase);foreach(var c in cl){var k=NormalizeKey(c.Key);if(d.ContainsKey(k))throw new InvalidOperationException($"duplicate menu key: '{c.Key}'");d[k]=c;}return d;}
  public async Task RenderAsync(){ // Port of menu.py:53
   if(CurrentNodeSync is null&&CurrentNodeAsync is null)return;
   string t;List<Choice> cl;
   if(CurrentNodeAsync is not null)(t,cl)=await CurrentNodeAsync(Context).ConfigureAwait(false); else (t,cl)=CurrentNodeSync!(Context); // Port of menu.py:56
   _text=t;_choices=BuildChoices(cl);
  }
  public string GetDisplay(){ // Port of menu.py:68
   if(CurrentNodeSync is null&&CurrentNodeAsync is null)return "";
   var lines=new List<string>{$"\n{_text}"}; foreach(var c in _choices.Values)lines.Add($"  [{c.Key}] {c.Desc}"); return string.Join("\r\n",lines); // Port of menu.py:71
  }
  // Shared input prefix for the sync/async handlers: normalization and lookup
  // (menu.py:81-82). Null means "no such key" (stay); the callback/goto/Stay
  // dispatch below stays per-handler (sync throws inline, async faults).
  internal static string NormalizeKey(string s)=>s.ToLowerInvariant().Trim();
  Choice? TryGetChoice(string clean)=>_choices.TryGetValue(clean,out var ch)?ch:null;
  public bool HandleInput(string input){ // Port of menu.py:76
   if(_choices.Count==0){CurrentNodeSync=null;CurrentNodeAsync=null;return false;} // Port of menu.py:77
   var ch=TryGetChoice(NormalizeKey(input)); if(ch is null)return true; // Port of menu.py:81-82
  if(ch.CallbackSync is not null||ch.CallbackAsync is not null){try{if(ch.CallbackAsync is not null)throw new InvalidOperationException("async callback requires async handle_input");ch.CallbackSync?.Invoke(Context);}catch{try{AtherizLogger.LogError("menu callback failed");}catch{}}} // Port of menu.py:85-91
  if(ch.GotoSync is not null||ch.GotoAsync is not null){if(ch.GotoAsync is not null)throw new InvalidOperationException("async goto requires async handle_input");CurrentNodeSync=ch.GotoSync;CurrentNodeAsync=null;_Render();return true;} // Port of menu.py:92
  if(ch.Stay){_Render();return true;} // Port of menu.py:96
  CurrentNodeSync=null;CurrentNodeAsync=null;return false; // Port of menu.py:99
 }
  public async Task<bool> HandleInputAsync(string input){ // Port of menu.py:102
   if(_choices.Count==0){CurrentNodeSync=null;CurrentNodeAsync=null;return false;}
   var ch=TryGetChoice(NormalizeKey(input)); if(ch is null)return true;
  if(ch.CallbackSync is not null||ch.CallbackAsync is not null){try{if(ch.CallbackAsync is not null)await ch.CallbackAsync(Context).ConfigureAwait(false);else ch.CallbackSync?.Invoke(Context);}catch{try{AtherizLogger.LogError("menu callback failed");}catch{}}} // Port of menu.py:110
  if(ch.GotoSync is not null||ch.GotoAsync is not null){CurrentNodeSync=ch.GotoSync;CurrentNodeAsync=ch.GotoAsync;await RenderAsync().ConfigureAwait(false);return true;} // Port of menu.py:118
  if(ch.Stay){await RenderAsync().ConfigureAwait(false);return true;}
  CurrentNodeSync=null;CurrentNodeAsync=null;return false;
 }
 public void Close(){CurrentNodeSync=null;CurrentNodeAsync=null;_text="";_choices.Clear();Context.State.Clear();} // Port of menu.py:128
 public bool HasNode=>CurrentNodeSync is not null||CurrentNodeAsync is not null;
 public IReadOnlyDictionary<string,Choice> CurrentChoices=>_choices; public string CurrentText=>_text;
}
// Port of atheriz/menu.py:135 run_menu + spec Menu class
public sealed class Menu{
 public string Prompt{get;set;}=""; // spec
  public Dictionary<string,Func<Session,string,Task<bool>>> Options{get;}=new(StringComparer.OrdinalIgnoreCase);
  // Optional per-option descriptions (spec-Menu has no Python original; the Options dict
  // shape is spec-fixed, so descs ride alongside instead of changing the value type).
  public Dictionary<string,string> OptionDescs{get;}=new(StringComparer.OrdinalIgnoreCase);
 public TimeSpan Timeout{get;set;}=TimeSpan.FromSeconds(AtherizSettings.Global.MenuPromptTimeout); // Port of settings.py:140
 public Menu(){} public Menu(string p,Dictionary<string,Func<Session,string,Task<bool>>>? opts=null,TimeSpan? to=null){Prompt=p;if(opts is not null)foreach(var kv in opts)Options[kv.Key]=kv.Value;if(to.HasValue)Timeout=to.Value;}
 public async Task<bool> Run(Session session,string promptText){ // Port of menu.py:135-149
  string cur=string.IsNullOrEmpty(promptText)?Prompt:promptText;
  while(true){
    var display=cur; if(Options.Count>0){var lines=new List<string>{$"\n{display}"}; foreach(var kv in Options)lines.Add(OptionDescs.TryGetValue(kv.Key,out var dd)?$"  [{kv.Key}] {dd}":$"  [{kv.Key}]"); display=string.Join("\r\n",lines);}
   var inp = await MenuPrompt.PromptWithTimeoutAsync(session, display, Timeout).ConfigureAwait(false); if(inp is null)break; // Port of menu.py:153-156 via MenuPrompt
   var clean=MenuEngine.NormalizeKey(inp); if(!Options.TryGetValue(clean,out var h)){try{AtherizLogger.LogDebug($"menu unknown key: {clean}");}catch{} continue;} // Port of menu.py:82
   try{var keepGoing=await h(session,inp).ConfigureAwait(false); if(!keepGoing)return true;}catch(Exception ex){try{AtherizLogger.LogError($"menu handle_input failed: {ex}");}catch{} break;} // Port of menu.py:85; handler true = keep prompting, false = done (Run true = exited)
  } return false;
 }
 public static Task RunMenu(Session s,Menu m,string p)=>m.Run(s,p); // Port of menu.py:135
 public static Task RunMenu(Session s,string p,Dictionary<string,Func<Session,string,Task<bool>>> opts,TimeSpan? to=null){var m=new Menu(p,opts,to); return m.Run(s,p);}
}
public static class MenuRunner{ // Port of menu.py:135 top-level run_menu future-based
 static Session? GetSess(object? caller){ if(caller is ISessionProvider p){ try{ return p.Session; }catch{ return null; } } return null;}
 // Shared prompt loop for the sync/async overloads below: display, timeout
 // prompt, handle, log-and-break, close. Render/handle ride as delegates —
 // the sync overload's ctor already rendered and its handler is sync.
 static async Task RunLoopAsync(MenuEngine e,object? caller,Func<string,Task<bool>> handle){
  try{while(e.HasNode){var d=e.GetDisplay(); var sess=GetSess(caller); if(sess is null)break; var to=TimeSpan.FromSeconds(AtherizSettings.Global.MenuPromptTimeout); var inp = await MenuPrompt.PromptWithTimeoutAsync(sess, d, to).ConfigureAwait(false); if(inp is null)break; try{var k=await handle(inp).ConfigureAwait(false); if(!k)break;}catch{try{AtherizLogger.LogError("menu handle_input failed");}catch{} break;}} }finally{e.Close();}}
 public static Task RunMenuAsync(object? caller,Func<MenuContext,(string,List<Choice>)> start){ // Port of menu.py:140-166
  return Task.Run(async()=>{var e=new MenuEngine(caller,start); await RunLoopAsync(e,caller,s=>Task.FromResult(e.HandleInput(s))).ConfigureAwait(false);});}
 public static Task RunMenuAsync(object? caller,Func<MenuContext,Task<(string,List<Choice>)>> startA){
  return Task.Run(async()=>{var e=new MenuEngine(caller,startA); try{await e.RenderAsync().ConfigureAwait(false);}catch{try{AtherizLogger.LogError("menu initial render failed");}catch{} e.Close(); return;} await RunLoopAsync(e,caller,e.HandleInputAsync).ConfigureAwait(false);});}
}

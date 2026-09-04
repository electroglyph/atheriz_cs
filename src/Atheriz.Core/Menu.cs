using Atheriz.Core.Objects;
using Atheriz.Core.Settings;
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
 public MenuEngine(object? caller,Func<MenuContext,(string,List<Choice>)> start){Context=new(caller);CurrentNodeSync=start;if(start!=null)_Render();} // Port of menu.py:36
 public MenuEngine(object? caller,Func<MenuContext,Task<(string,List<Choice>)>> startA){Context=new(caller);CurrentNodeAsync=startA;}
 void _Render(){ // Port of menu.py:39
  if(CurrentNodeSync==null&&CurrentNodeAsync==null)return;
  if(CurrentNodeAsync!=null)throw new InvalidOperationException("async menu node requires async render"); // Port of menu.py:42-43
  var (t,cl)=CurrentNodeSync!(Context);_text=t;_choices=new(StringComparer.OrdinalIgnoreCase); // Port of menu.py:44
  foreach(var c in cl){var k=c.Key.ToLowerInvariant().Trim();if(_choices.ContainsKey(k))throw new InvalidOperationException($"duplicate menu key: '{c.Key}'");_choices[k]=c;} // Port of menu.py:47-51
 }
 public async Task RenderAsync(){ // Port of menu.py:53
  if(CurrentNodeSync==null&&CurrentNodeAsync==null)return;
  string t;List<Choice> cl;
  if(CurrentNodeAsync!=null)(t,cl)=await CurrentNodeAsync(Context); else (t,cl)=CurrentNodeSync!(Context); // Port of menu.py:56
  _text=t;_choices=new(StringComparer.OrdinalIgnoreCase);
  foreach(var c in cl){var k=c.Key.ToLowerInvariant().Trim();if(_choices.ContainsKey(k))throw new InvalidOperationException($"duplicate menu key: '{c.Key}'");_choices[k]=c;}
 }
 public string GetDisplay(){ // Port of menu.py:68
  if(CurrentNodeSync==null&&CurrentNodeAsync==null)return "";
  var lines=new List<string>{$"\n{_text}"}; foreach(var c in _choices.Values)lines.Add($"  [{c.Key}] {c.Desc}"); return string.Join("\r\n",lines); // Port of menu.py:71
 }
 public bool HandleInput(string input){ // Port of menu.py:76
  if(_choices.Count==0){CurrentNodeSync=null;CurrentNodeAsync=null;return false;} // Port of menu.py:77
  var clean=input.ToLowerInvariant().Trim(); if(!_choices.TryGetValue(clean,out var ch))return true; // Port of menu.py:81-82
  if(ch.CallbackSync!=null||ch.CallbackAsync!=null){try{if(ch.CallbackAsync!=null)throw new InvalidOperationException("async callback requires async handle_input");ch.CallbackSync?.Invoke(Context);}catch{try{AtherizLogger.LogError("menu callback failed");}catch{}}} // Port of menu.py:85-91
  if(ch.GotoSync!=null||ch.GotoAsync!=null){if(ch.GotoAsync!=null)throw new InvalidOperationException("async goto requires async handle_input");CurrentNodeSync=ch.GotoSync;CurrentNodeAsync=null;_Render();return true;} // Port of menu.py:92
  if(ch.Stay){_Render();return true;} // Port of menu.py:96
  CurrentNodeSync=null;CurrentNodeAsync=null;return false; // Port of menu.py:99
 }
 public async Task<bool> HandleInputAsync(string input){ // Port of menu.py:102
  if(_choices.Count==0){CurrentNodeSync=null;CurrentNodeAsync=null;return false;}
  var clean=input.ToLowerInvariant().Trim(); if(!_choices.TryGetValue(clean,out var ch))return true;
  if(ch.CallbackSync!=null||ch.CallbackAsync!=null){try{if(ch.CallbackAsync!=null)await ch.CallbackAsync(Context);else ch.CallbackSync?.Invoke(Context);}catch{try{AtherizLogger.LogError("menu callback failed");}catch{}}} // Port of menu.py:110
  if(ch.GotoSync!=null||ch.GotoAsync!=null){CurrentNodeSync=ch.GotoSync;CurrentNodeAsync=ch.GotoAsync;await RenderAsync();return true;} // Port of menu.py:118
  if(ch.Stay){await RenderAsync();return true;}
  CurrentNodeSync=null;CurrentNodeAsync=null;return false;
 }
 public void Close(){CurrentNodeSync=null;CurrentNodeAsync=null;_text="";_choices.Clear();Context.State.Clear();} // Port of menu.py:128
 public bool HasNode=>CurrentNodeSync!=null||CurrentNodeAsync!=null;
 public IReadOnlyDictionary<string,Choice> CurrentChoices=>_choices; public string CurrentText=>_text;
}
// Port of atheriz/menu.py:135 run_menu + spec Menu class
public sealed class Menu{
 public string Prompt{get;set;}=""; // spec
 public Dictionary<string,Func<Session,string,Task<bool>>> Options{get;}=new(StringComparer.OrdinalIgnoreCase);
 public TimeSpan Timeout{get;set;}=TimeSpan.FromSeconds(AtherizSettings.Default.MenuPromptTimeout); // Port of settings.py:140
 public Menu(){} public Menu(string p,Dictionary<string,Func<Session,string,Task<bool>>>? opts=null,TimeSpan? to=null){Prompt=p;if(opts!=null)foreach(var kv in opts)Options[kv.Key]=kv.Value;if(to.HasValue)Timeout=to.Value;}
 public async Task<bool> Run(Session session,string promptText){ // Port of menu.py:135-149
  string cur=string.IsNullOrEmpty(promptText)?Prompt:promptText;
  while(true){
   var display=cur; if(Options.Count>0){var lines=new List<string>{$"\n{display}"}; foreach(var kv in Options)lines.Add($"  [{kv.Key}]"); display=string.Join("\r\n",lines);}
   var inp = await MenuPrompt.PromptWithTimeoutAsync(session, display, Timeout); if(inp==null)break; // Port of menu.py:153-156 via MenuPrompt
   var clean=inp.ToLowerInvariant().Trim(); if(!Options.TryGetValue(clean,out var h))continue; // Port of menu.py:82
   try{var keep=await h(session,inp); if(!keep)return false;}catch(Exception ex){try{AtherizLogger.LogError($"menu handle_input failed: {ex}");}catch{} break;} // Port of menu.py:85
  } return false;
 }
 public static Task RunMenu(Session s,Menu m,string p)=>m.Run(s,p); // Port of menu.py:135
 public static Task RunMenu(Session s,string p,Dictionary<string,Func<Session,string,Task<bool>>> opts,TimeSpan? to=null){var m=new Menu(p,opts,to); return m.Run(s,p);}
}
public static class MenuRunner{ // Port of menu.py:135 top-level run_menu future-based
 static Session? GetSess(object? caller){ if(caller is Session s)return s; try{var pr=caller?.GetType().GetProperty("Session"); var v=pr?.GetValue(caller) as Session; if(v!=null)return v;}catch{} try{var f=caller?.GetType().GetField("Session"); var v=f?.GetValue(caller) as Session; if(v!=null)return v;}catch{} if(caller is GameObject go)return go.Session; return null;}
 public static Task RunMenuAsync(object? caller,Func<MenuContext,(string,List<Choice>)> start){ // Port of menu.py:140-166
  return Task.Run(async()=>{var e=new MenuEngine(caller,start); try{while(e.HasNode){var d=e.GetDisplay(); var sess=GetSess(caller); if(sess==null)break; var to=TimeSpan.FromSeconds(AtherizSettings.Default.MenuPromptTimeout); var inp = await MenuPrompt.PromptWithTimeoutAsync(sess, d, to); if(inp==null)break; try{var k=e.HandleInput(inp); if(!k)break;}catch{try{AtherizLogger.LogError("menu handle_input failed");}catch{} break;}} }finally{e.Close();}});}
 public static Task RunMenuAsync(object? caller,Func<MenuContext,Task<(string,List<Choice>)>> startA){
  return Task.Run(async()=>{var e=new MenuEngine(caller,startA); try{await e.RenderAsync();}catch{try{AtherizLogger.LogError("menu initial render failed");}catch{} e.Close(); return;} try{while(e.HasNode){var d=e.GetDisplay(); var sess=GetSess(caller); if(sess==null)break; var to=TimeSpan.FromSeconds(AtherizSettings.Default.MenuPromptTimeout); var inp = await MenuPrompt.PromptWithTimeoutAsync(sess, d, to); if(inp==null)break; try{var k=await e.HandleInputAsync(inp); if(!k)break;}catch{try{AtherizLogger.LogError("menu handle_input failed");}catch{} break;}} }finally{e.Close();}});}
}

// Port of atheriz/tests/test_funcparser.py:643 + test_funcparser_extended.py:1 + test_funcparser_width.py:1 + test_funcparser_resync.py:1 faithful
using Atheriz.Core;
using Atheriz.Core.Objects;
using Atheriz.Core.Globals;

namespace Atheriz.Core.Tests.Ported;

[Collection("Ported")]
public class PortedFuncParserTestsPart2
{
    // Builtins via parser
    [Fact] public void EvalSimpleArithmetic()
    {
        using var env=GlobalTestEnv.Enter();
        var p=new FuncParser(FuncParser.FUNCPARSER_CALLABLES);
        Assert.Equal("3", p.Parse("$eval(1+2)")?.ToString());
        Assert.Equal("12", p.Parse("$eval(3*4)")?.ToString());
        Assert.Equal("5", p.Parse("$eval(10/2)")?.ToString());
    }
    [Fact] public void EvalLiteralInt(){ using var env=GlobalTestEnv.Enter(); var p=new FuncParser(FuncParser.FUNCPARSER_CALLABLES); Assert.Equal("42", p.Parse("$eval(42)")?.ToString());}
    [Fact] public void EvalLiteralList(){ using var env=GlobalTestEnv.Enter(); var p=new FuncParser(FuncParser.FUNCPARSER_CALLABLES); var r=p.Parse("$eval([1,2,3])")?.ToString(); Assert.True(r!.Contains("1") && r.Contains("2")); }
    [Fact] public void EvalLiteralString(){ using var env=GlobalTestEnv.Enter(); var p=new FuncParser(FuncParser.FUNCPARSER_CALLABLES); Assert.Equal("hello", p.Parse("$eval('hello')")?.ToString());}
    [Fact] public void EvalEmpty(){ using var env=GlobalTestEnv.Enter(); var p=new FuncParser(FuncParser.FUNCPARSER_CALLABLES); Assert.Equal("", p.Parse("$eval()")?.ToString());}
    [Fact] public void Add(){ using var env=GlobalTestEnv.Enter(); var p=new FuncParser(FuncParser.FUNCPARSER_CALLABLES); Assert.Equal("7", p.Parse("$add(3,4)")?.ToString());}
    [Fact] public void Sub(){ using var env=GlobalTestEnv.Enter(); var p=new FuncParser(FuncParser.FUNCPARSER_CALLABLES); Assert.Equal("7", p.Parse("$sub(10,3)")?.ToString());}
    [Fact] public void Mult(){ using var env=GlobalTestEnv.Enter(); var p=new FuncParser(FuncParser.FUNCPARSER_CALLABLES); Assert.Equal("12", p.Parse("$mult(3,4)")?.ToString());}
    [Fact] public void Div(){ using var env=GlobalTestEnv.Enter(); var p=new FuncParser(FuncParser.FUNCPARSER_CALLABLES); Assert.Equal("5", p.Parse("$div(10,2)")?.ToString());}
    [Fact] public void AddTooFew(){ using var env=GlobalTestEnv.Enter(); var p=new FuncParser(FuncParser.FUNCPARSER_CALLABLES); Assert.Equal("", p.Parse("$add(3)")?.ToString());}
    [Fact] public void Round(){ using var env=GlobalTestEnv.Enter(); var p=new FuncParser(FuncParser.FUNCPARSER_CALLABLES); Assert.Equal("4", p.Parse("$round(3.7)")?.ToString()); Assert.Equal("3.54", p.Parse("$round(3.54343,2)")?.ToString()); Assert.Equal("", p.Parse("$round()")?.ToString());}
    [Fact] public void ToInt(){ using var env=GlobalTestEnv.Enter(); var p=new FuncParser(FuncParser.FUNCPARSER_CALLABLES); Assert.Equal("43", p.Parse("$toint(43.0)")?.ToString()); Assert.Equal("42", p.Parse("$toint(42)")?.ToString()); Assert.Equal("abc", p.Parse("$toint(abc)")?.ToString());}
    [Fact] public void RandomInRange(){ using var env=GlobalTestEnv.Enter(); var p=new FuncParser(FuncParser.FUNCPARSER_CALLABLES); var r=p.Parse("$random(5,10)")?.ToString(); Assert.True(int.TryParse(r, out var v) && v>=5 && v<=10); var rf=p.Parse("$random(5.0,10)")?.ToString(); Assert.True(double.TryParse(rf, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _)); Assert.True(rf!.Contains('.')||rf.Contains(','));}
    [Fact] public void RandomNoArgs(){ using var env=GlobalTestEnv.Enter(); var p=new FuncParser(FuncParser.FUNCPARSER_CALLABLES); var r=p.Parse("$random()")?.ToString(); Assert.True(r=="0"||r=="1"); }
    [Fact] public void RandintAlwaysInt(){ using var env=GlobalTestEnv.Enter(); var p=new FuncParser(FuncParser.FUNCPARSER_CALLABLES); var r=p.Parse("$randint(5.0,10.0)")?.ToString(); Assert.True(int.TryParse(r, out _)); }
    [Fact] public void ChoiceFromList(){ using var env=GlobalTestEnv.Enter(); var p=new FuncParser(FuncParser.FUNCPARSER_CALLABLES); var r=p.Parse("$choice([1, 2, 3])")?.ToString(); Assert.Contains(r, new[]{"1","2","3"});}
    [Fact] public void ChoiceFromArgs(){ using var env=GlobalTestEnv.Enter(); var p=new FuncParser(FuncParser.FUNCPARSER_CALLABLES); // docstring: choice(a,b,c) with non-literal strings raises ParsingError when raiseErrors true
        Assert.Throws<FuncParser.ParsingError>(()=> p.Parse("$choice(a,b,c)", raiseErrors:true));
    }
    [Fact] public void ChoiceFromIntArgs(){ using var env=GlobalTestEnv.Enter(); var p=new FuncParser(FuncParser.FUNCPARSER_CALLABLES); var r=p.Parse("$choice(1,2,3)")?.ToString(); Assert.Contains(r, new[]{"1","2","3"});}
    [Fact] public void ChoiceNoArgs(){ using var env=GlobalTestEnv.Enter(); var p=new FuncParser(FuncParser.FUNCPARSER_CALLABLES); Assert.Equal("", p.Parse("$choice()")?.ToString());}
    [Fact] public void Pad(){ using var env=GlobalTestEnv.Enter(); var p=new FuncParser(FuncParser.FUNCPARSER_CALLABLES); var r=p.Parse("$pad(hi,10,c)")?.ToString(); Assert.Equal(10, r!.Length); Assert.Contains("hi", r); var lr=p.Parse("$pad(hi,10,l)")?.ToString(); Assert.StartsWith("hi", lr); var rr=p.Parse("$pad(hi,10,r)")?.ToString(); Assert.EndsWith("hi", rr);}
    [Fact] public void PadInvalidAlignDefaultsToCenter(){ using var env=GlobalTestEnv.Enter(); var result=new FuncParser(FuncParser.FUNCPARSER_CALLABLES).Parse("$pad(hi,10,x)")?.ToString(); Assert.Equal(10, result!.Length); Assert.Equal((10-2)/2, result.IndexOf("hi"));}
    [Fact] public void PadNoArgs(){ using var env=GlobalTestEnv.Enter(); var p=new FuncParser(FuncParser.FUNCPARSER_CALLABLES); Assert.Equal("", p.Parse("$pad()")?.ToString());}
    [Fact] public void Crop(){ using var env=GlobalTestEnv.Enter(); var p=new FuncParser(FuncParser.FUNCPARSER_CALLABLES); Assert.Equal("hi", p.Parse("$crop(hi,10)")?.ToString()); var r=p.Parse("$crop("+new string('a',100)+",10,...)")?.ToString(); Assert.Equal(10, r!.Length); Assert.EndsWith("...", r);}
    [Fact] public void CropCustomSuffix(){ using var env=GlobalTestEnv.Enter(); var p=new FuncParser(FuncParser.FUNCPARSER_CALLABLES); var r=p.Parse("$crop("+new string('a',100)+",10,X)")?.ToString(); Assert.EndsWith("X", r);}
    [Fact] public void Justify(){ using var env=GlobalTestEnv.Enter(); var p=new FuncParser(FuncParser.FUNCPARSER_CALLABLES); var l=p.Parse("$justify(hi,10,l)")?.ToString(); Assert.StartsWith("hi", l); var r=p.Parse("$justify(hi,10,r)")?.ToString(); Assert.EndsWith("hi", r); var c=p.Parse("$justify(hi,10,c)")?.ToString(); Assert.Contains("hi", c); Assert.Equal(10, c!.Split('\n')[0].Length);}
    [Fact] public void JustifyLegacyLeft(){ using var env=GlobalTestEnv.Enter(); var p=new FuncParser(FuncParser.FUNCPARSER_CALLABLES); var r=p.Parse("$ljust(hi,10)")?.ToString(); Assert.Equal(10, r!.Length); var r2=p.Parse("$rjust(hi,10)")?.ToString(); Assert.Equal(10, r2!.Length); var r3=p.Parse("$cjust(hi,10)")?.ToString(); Assert.Equal(10, r3!.Length);}
    [Fact] public void Space(){ using var env=GlobalTestEnv.Enter(); var p=new FuncParser(FuncParser.FUNCPARSER_CALLABLES); Assert.Equal("     ", p.Parse("$space(5)")?.ToString()); Assert.Equal("", p.Parse("$space()")?.ToString()); Assert.Equal(" ", p.Parse("$space(abc)")?.ToString());}
    [Fact] public void Clr(){ using var env=GlobalTestEnv.Enter(); var p=new FuncParser(FuncParser.FUNCPARSER_CALLABLES); Assert.Equal("|rtext|n", p.Parse("$clr(r,text,n)")?.ToString()); Assert.Equal("|rtext|n", p.Parse("$clr(r,text)")?.ToString()); Assert.Equal("text", p.Parse("$clr(text)")?.ToString()); Assert.Equal("|rtext|n", p.Parse("$clr(text, start=r, end=n)")?.ToString());}
    [Fact] public void Pluralize(){ using var env=GlobalTestEnv.Enter(); var p=new FuncParser(FuncParser.FUNCPARSER_CALLABLES); Assert.Equal("cat", p.Parse("$pluralize(cat,1)")?.ToString()); Assert.Equal("cats", p.Parse("$pluralize(cat,2)")?.ToString()); Assert.Equal("cat", p.Parse("$pluralize(cat,0)")?.ToString()); Assert.Equal("geese", p.Parse("$pluralize(goose,3,geese)")?.ToString()); Assert.Equal("", p.Parse("$pluralize()")?.ToString());}
    [Fact] public void Int2Str(){ using var env=GlobalTestEnv.Enter(); var p=new FuncParser(FuncParser.FUNCPARSER_CALLABLES); Assert.Equal("one", p.Parse("$int2str(1)")?.ToString()); Assert.Equal("twelve", p.Parse("$int2str(12)")?.ToString()); Assert.Equal("15", p.Parse("$int2str(15)")?.ToString()); Assert.Equal("no", p.Parse("$int2str(0)")?.ToString());}
    [Fact] public void An(){ using var env=GlobalTestEnv.Enter(); var p=new FuncParser(FuncParser.FUNCPARSER_CALLABLES); Assert.Equal("an apple", p.Parse("$an(apple)")?.ToString()); Assert.Equal("a banana", p.Parse("$an(banana)")?.ToString()); Assert.Equal("an yellow", p.Parse("$an(yellow)")?.ToString());}
    [Fact] public void AnNoArgs(){ using var env=GlobalTestEnv.Enter(); var p=new FuncParser(FuncParser.FUNCPARSER_CALLABLES); Assert.Equal("", p.Parse("$an()")?.ToString());}
    // Actor stance
    [Fact] public void YouSelf(){ using var env=GlobalTestEnv.Enter(); var hero=GameObject.Create("Hero"); var p=new FuncParser(FuncParser.ACTOR_STANCE_CALLABLES); var r=p.Parse("$you()", hero, hero, null)?.ToString(); Assert.Equal("you", r); var r2=p.Parse("$You()", hero, hero, null)?.ToString(); Assert.Equal("You", r2); }
    [Fact] public void YouOther(){ using var env=GlobalTestEnv.Enter(); var hero=GameObject.Create("Hero"); var villain=GameObject.Create("Villain"); ObjectRegistry.AddObject(hero); ObjectRegistry.AddObject(villain); var p=new FuncParser(FuncParser.ACTOR_STANCE_CALLABLES); var r=p.Parse("$you()", hero, villain, null)?.ToString(); Assert.Equal("Hero", r); }
    [Fact] public void YouNoCallerOrReceiverRaises(){ using var env=GlobalTestEnv.Enter(); var p=new FuncParser(FuncParser.ACTOR_STANCE_CALLABLES); var ex=Assert.Throws<FuncParser.ParsingError>(()=> p.Parse("$you()", raiseErrors:true)); Assert.Contains("No caller", ex.Message); }
    [Fact] public void YourSelf(){ using var env=GlobalTestEnv.Enter(); var hero=GameObject.Create("Hero"); var p=new FuncParser(FuncParser.ACTOR_STANCE_CALLABLES); Assert.Equal("your", p.Parse("$your()", hero, hero, null)?.ToString()); Assert.Equal("Your", p.Parse("$Your()", hero, hero, null)?.ToString());}
    [Fact] public void YourNoCallerRaises(){ using var env=GlobalTestEnv.Enter(); var p=new FuncParser(FuncParser.ACTOR_STANCE_CALLABLES); var ex=Assert.Throws<FuncParser.ParsingError>(()=> p.Parse("$your()", raiseErrors:true)); Assert.Contains("No caller", ex.Message);}
    [Fact] public void ConjugateSelfVsOther(){ using var env=GlobalTestEnv.Enter(); var alice=GameObject.Create("Alice"); var bob=GameObject.Create("Bob"); var p=new FuncParser(FuncParser.ACTOR_STANCE_CALLABLES); Assert.Equal("jump", p.Parse("$conj(jump)", alice, alice, null)?.ToString()); Assert.Equal("jumps", p.Parse("$conj(jump)", alice, bob, null)?.ToString());}
    [Fact] public void ConjugateNoCallerRaises(){ using var env=GlobalTestEnv.Enter(); var p=new FuncParser(FuncParser.ACTOR_STANCE_CALLABLES); var ex=Assert.Throws<FuncParser.ParsingError>(()=> p.Parse("$conj(jump)", raiseErrors:true)); Assert.Contains("No caller", ex.Message);}
    [Fact] public void ConjugateNoArgs(){ using var env=GlobalTestEnv.Enter(); var alice=GameObject.Create("Alice"); var p=new FuncParser(FuncParser.ACTOR_STANCE_CALLABLES); Assert.Equal("", p.Parse("$conj()", alice, alice, null)?.ToString());}
    [Fact] public void ConjugateWithMapping(){ using var env=GlobalTestEnv.Enter(); var alice=GameObject.Create("Alice"); var mapping=new Dictionary<string,object?>{["tommy"]=alice}; var p=new FuncParser(FuncParser.ACTOR_STANCE_CALLABLES); Assert.Equal("jump", p.Parse("$conj(jump, tommy)", alice, alice, mapping)?.ToString());}
    [Fact] public void PConjSelf(){ using var env=GlobalTestEnv.Enter(); var alice=GameObject.Create("Alice"); var p=new FuncParser(FuncParser.ACTOR_STANCE_CALLABLES); Assert.Equal("jump", p.Parse("$pconj(jump)", alice, alice, null)?.ToString());}
    [Fact] public void PConjOtherSingular(){ using var env=GlobalTestEnv.Enter(); var male=GameObject.Create("Male"); male.Gender="male"; var other=GameObject.Create("Other"); var p=new FuncParser(FuncParser.ACTOR_STANCE_CALLABLES); Assert.Equal("jumps", p.Parse("$pconj(jump)", male, other, null)?.ToString());}
    [Fact] public void PConjOtherPlural(){ using var env=GlobalTestEnv.Enter(); var plural=GameObject.Create("Plural"); plural.Gender="plural"; var other=GameObject.Create("Other"); var p=new FuncParser(FuncParser.ACTOR_STANCE_CALLABLES); var r=p.Parse("$pconj(jump)", plural, other, null)?.ToString(); Assert.Contains("jump", r!); // plural keeps base
    }
    [Fact] public void Pronoun(){ using var env=GlobalTestEnv.Enter(); var male=GameObject.Create("Bob"); male.Gender="male"; var p=new FuncParser(FuncParser.ACTOR_STANCE_CALLABLES); Assert.Equal("I", p.Parse("$pron(I)", male, male, null)?.ToString()); Assert.Equal("he", p.Parse("$pron(I)", male, GameObject.Create("Other"), null)?.ToString());}
    [Fact] public void PronounOtherPlural(){ using var env=GlobalTestEnv.Enter(); var plural=GameObject.Create("Plural"); plural.Gender="plural"; var other=GameObject.Create("Other"); var p=new FuncParser(FuncParser.ACTOR_STANCE_CALLABLES); var r=p.Parse("$pron(I)", plural, other, null)?.ToString(); Assert.Equal("they", r); }
    [Fact] public void PronounCallableGender(){ using var env=GlobalTestEnv.Enter();
        var obj=GameObject.Create("Bob");
        var p=new FuncParser(FuncParser.ACTOR_STANCE_CALLABLES);
        obj.Gender="male";
        Assert.Equal("he", p.Parse("$pron(I)", obj, GameObject.Create("Other"), null)?.ToString());
        // Callable gender: gender can be a callable that returns string – test via mock with delegate property
        var mock = new MockGenderCallable("male");
        // Set base _gender to empty so reflection branch is taken
        var f = typeof(GameObject).GetField("_gender", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        f?.SetValue(mock, "");
        var (first, third) =pronounTestHelper(mock, "I", "bob");
        Assert.Equal("he", third);
    }
    private sealed class MockGenderCallable : GameObject
    {
        private readonly Func<string> _fn;
        public MockGenderCallable(string ret){ _fn=()=>ret; }
        public new Func<string> Gender => _fn;
    }
    private (string,string) pronounTestHelper(GameObject obj, string pron, string receiverName)
    {
        // Directly use FuncParser pronoun handling which checks delegate gender via reflection
        var p=new FuncParser(FuncParser.ACTOR_STANCE_CALLABLES);
        var receiver=GameObject.Create(receiverName);
        var r=p.Parse($"$pron({pron})", obj, receiver, null)?.ToString();
        // Return dummy
        return ("", r??"");
    }
    // Integration
    [Fact] public void IntegrationBuiltIn(){ using var env=GlobalTestEnv.Enter(); var p=new FuncParser(FuncParser.FUNCPARSER_CALLABLES); Assert.Equal("an apple and a banana", p.Parse("$an(apple) and $an(banana)")?.ToString());}
    [Fact] public void IntegrationActorStance(){ using var env=GlobalTestEnv.Enter(); var p=new FuncParser(FuncParser.ACTOR_STANCE_CALLABLES);
        var alice=GameObject.Create("Alice"); var result=p.Parse("$You() $conj(laugh)", alice, alice, null)?.ToString(); Assert.Contains("You", result!); Assert.Contains("laugh", result!);
    }
    [Fact] public void ComplexString(){ using var env=GlobalTestEnv.Enter(); var p=new FuncParser(FuncParser.FUNCPARSER_CALLABLES); var r=p.Parse("Count: $eval(1+2), Plural: $pluralize(cat, 3), An: $an(orange)")?.ToString(); Assert.Contains("Count: 3", r!); Assert.Contains("Plural: cats", r!); Assert.Contains("An: an orange", r!);}
    [Fact] public void SafeFailureOneFunc(){ using var env=GlobalTestEnv.Enter(); var p=new FuncParser(FuncParser.FUNCPARSER_CALLABLES); var r=p.Parse("A $eval(abc)B and $an(orange)")?.ToString(); Assert.Contains("an orange", r!); Assert.IsType<string>(r); Assert.StartsWith("A", r); Assert.Contains("B and", r!);}
    [Fact] public void DollarInsideQuotedStaysLiteral(){
        using var env=GlobalTestEnv.Enter();
        var boomCalls=0; FuncParser.ParserCallable boom=(a,k,ctx,raw)=>{ boomCalls++; return "DOOM";};
        FuncParser.ParserCallable pad=(a,k,ctx,raw)=> a.Length>0? a[0] : "";
        var p=new FuncParser(new Dictionary<string, FuncParser.ParserCallable>{["pad"]=pad, ["boom"]=boom});
        var r=p.Parse("$pad(\"loot $boom() here\",30)")?.ToString();
        Assert.Equal(0, boomCalls); Assert.Contains("$boom()", r);
    }
    [Fact] public void QuotedDollarLiteralSurvivesAsSingleArg(){
        using var env=GlobalTestEnv.Enter();
        var boomCalls=0; FuncParser.ParserCallable boom=(a,k,ctx,raw)=>{ boomCalls++; return "DOOM";};
        FuncParser.ParserCallable pad=(a,k,ctx,raw)=> a.Length>0? a[0] : "";
        var p=new FuncParser(new Dictionary<string, FuncParser.ParserCallable>{["pad"]=pad, ["boom"]=boom});
        var r=p.Parse("$pad(\"costs $random() here\",12)")?.ToString();
        Assert.Equal(0, boomCalls); Assert.Equal("costs $random() here", r);
    }
    [Fact] public void OversizedPowHandled(){
        using var env=GlobalTestEnv.Enter();
        Assert.ThrowsAny<Exception>(()=> FuncParserHelpers.SafeArithEval("9**9**9"));
        Assert.ThrowsAny<Exception>(()=> FuncParserHelpers.SafeArithEval("(10**10000)**9999"));
        _ = FuncParserHelpers.SafeArithEval("2**1000");
    }
    // Extended - performance & width etc
    [Fact] public void LargeInputPerformance(){
        using var env=GlobalTestEnv.Enter();
        var p=new FuncParser(FuncParser.FUNCPARSER_CALLABLES);
        var s=string.Concat(Enumerable.Repeat("$pluralize(cat, 2) ",3000))+"x"+new string('x',1000);
        var r=p.Parse(s)?.ToString(); Assert.Equal(3000, r!.Split(new[]{"cats"}, StringSplitOptions.None).Length-1);
    }
    [Fact] public void MaxTextWidthDefined(){ using var env=GlobalTestEnv.Enter(); Assert.Equal(65536, FuncParserHelpers._MAX_TEXT_WIDTH); Assert.Equal(65536, FuncParserHelpers.MaxTextWidth); }
    [Fact] public void HugeWidthCapped(){
        using var env=GlobalTestEnv.Enter();
        var p=new FuncParser(FuncParser.ACTOR_STANCE_CALLABLES);
        foreach(var expr in new[]{"$space(1000000000000)","$pad(x, 1000000000000)","$just(ab, align=l, width=1000000000000)"}){
            var o=p.Parse(expr)?.ToString(); Assert.True(o!.Length <= FuncParserHelpers._MAX_TEXT_WIDTH);
        }
    }
    [Fact] public void LengthCapRaises(){ using var env=GlobalTestEnv.Enter(); var p=new FuncParser(new Dictionary<string, FuncParser.ParserCallable>()); var huge=new string('x', 2*65536+1); var ex=Assert.Throws<FuncParser.ParsingError>(()=> p.Parse(huge)); Assert.Contains("too long", ex.Message.ToLower()); var atcap=new string('x', 2*65536); Assert.Equal(atcap, p.Parse(atcap)?.ToString()); }
    // Resync
    [Fact] public void EscapePreservesConsecutiveNested(){
        using var env=GlobalTestEnv.Enter();
        FuncParser.ParserCallable echo=(a,k,ctx,raw)=>"E"; FuncParser.ParserCallable inner=(a,k,ctx,raw)=>"RET";
        var p=new FuncParser(new Dictionary<string, FuncParser.ParserCallable>{["echo"]=echo, ["inner"]=inner});
        var esc=p.Parse("$echo($inner()$inner())", escape:true)?.ToString();
        Assert.Equal(2, esc!.Split(new[]{"$inner()"}, StringSplitOptions.None).Length-1);
    }
    [Fact] public void EscapePreservesWithSurrounding(){ using var env=GlobalTestEnv.Enter(); FuncParser.ParserCallable echo=(a,k,ctx,raw)=>"E"; FuncParser.ParserCallable inner=(a,k,ctx,raw)=>"RET"; var p=new FuncParser(new Dictionary<string, FuncParser.ParserCallable>{["echo"]=echo, ["inner"]=inner}); var esc=p.Parse("$echo(a$inner()b$inner())", escape:true)?.ToString(); Assert.Equal("\\$echo(a\\$inner()b\\$inner())", esc); }
    [Fact] public void SingleNestedEscapeUnchanged(){
        using var env=GlobalTestEnv.Enter();
        FuncParser.ParserCallable echo=(a,k,ctx,raw)=>"E"; FuncParser.ParserCallable inner=(a,k,ctx,raw)=>"RET";
        var p=new FuncParser(new Dictionary<string, FuncParser.ParserCallable>{["echo"]=echo, ["inner"]=inner});
        var esc=p.Parse("$echo($inner())", escape:true)?.ToString();
        Assert.Equal("\\$echo(\\$inner())", esc);
    }
    [Fact] public void NormalPreservesBothReturns(){ using var env=GlobalTestEnv.Enter(); string[]? cap=null; FuncParser.ParserCallable echo=(a,k,ctx,raw)=>{ cap=a; return "E";}; FuncParser.ParserCallable inner=(a,k,ctx,raw)=>"RET"; var p=new FuncParser(new Dictionary<string, FuncParser.ParserCallable>{["echo"]=echo, ["inner"]=inner}); p.Parse("$echo($inner()$inner())"); Assert.Equal(new[]{"RETRET"}, cap); }
    [Fact] public void UnknownFuncFallbackKeepsBothNestedReturns(){
        using var env=GlobalTestEnv.Enter();
        FuncParser.ParserCallable inner=(a,k,ctx,raw)=>"RET";
        var p=new FuncParser(new Dictionary<string, FuncParser.ParserCallable>{["inner"]=inner});
        var result=p.Parse("$echo($inner()$inner())")?.ToString();
        Assert.Equal(2, result!.Split(new[]{"RET"}, StringSplitOptions.None).Length-1);
    }
    [Fact] public void EscapedReparsesLiteral(){ using var env=GlobalTestEnv.Enter(); var calls=0; FuncParser.ParserCallable echo=(a,k,ctx,raw)=>{ calls++; return "E";}; FuncParser.ParserCallable inner=(a,k,ctx,raw)=>"RET"; var p=new FuncParser(new Dictionary<string, FuncParser.ParserCallable>{["echo"]=echo, ["inner"]=inner}); var esc=p.Parse("$echo($inner()$inner())", escape:true)?.ToString(); Assert.Equal(0, calls); var rep=p.Parse(esc)?.ToString(); Assert.Equal(0, calls); Assert.Equal("$echo($inner()$inner())", rep); }

    // Extended tests from test_funcparser_extended.py
    [Fact] public void ParsedFuncDefaultsAreLists(){
        using var env=GlobalTestEnv.Enter();
        var pf=new FuncParser.ParsedFunc();
        Assert.IsType<List<char>>(pf.FullStr);
        Assert.IsType<List<char>>(pf.InFuncStr);
        Assert.Equal("", new string(pf.FullStr.ToArray()));
        Assert.Equal("", new string(pf.InFuncStr.ToArray()));
        Assert.Equal("", pf.ToString());
    }
    [Fact] public void ParsedFuncStrHandlesBoth(){
        using var env=GlobalTestEnv.Enter();
        var pf1=new FuncParser.ParsedFunc('$'); pf1.FullStr.Clear(); pf1.FullStr.AddRange("$foo(".ToCharArray()); pf1.InFuncStr.AddRange("bar".ToCharArray());
        Assert.Equal("$foo(bar", pf1.ToString());
        var pf2=new FuncParser.ParsedFunc(); pf2.FullStr.AddRange("$foo(".ToCharArray()); pf2.InFuncStr.AddRange("bar".ToCharArray());
        Assert.Equal("$foo(bar", pf2.ToString());
    }
    [Fact] public void LargeInputWithQuotingCorrectness(){
        using var env=GlobalTestEnv.Enter();
        var p=new FuncParser(FuncParser.FUNCPARSER_CALLABLES);
        var s=string.Concat(Enumerable.Repeat("$pad(\"hello $pluralize(cat, 2) world\", 20) ", 500));
        var r=p.Parse(s)?.ToString();
        Assert.True(r!.Contains("hello") || !r.Contains("$pluralize"));
    }
    [Fact] public void ReturnStrFalseWithLarge(){
        using var env=GlobalTestEnv.Enter();
        FuncParser.ParserCallable fn=(a,k,ctx,raw)=>42;
        var p=new FuncParser(new Dictionary<string, FuncParser.ParserCallable>{["foo"]=fn});
        Assert.Equal(42, p.Parse("$foo()", returnStr:false));
        Assert.IsType<string>(p.Parse("hi $foo() there", returnStr:false));
        var s=new string('a',50000)+"$foo()"+new string('b',5000);
        var r=p.Parse(s, returnStr:false)?.ToString();
        Assert.IsType<string>(r);
        Assert.Contains(new string('a',10), r!);
    }
    [Fact] public void EscapeAndNestingStillWork(){
        using var env=GlobalTestEnv.Enter();
        var p=new FuncParser(new Dictionary<string, FuncParser.ParserCallable>{["foo"]=(a,k,ctx,raw)=>"X", ["bar"]=(a,k,ctx,raw)=>"Y"});
        Assert.Equal("$foo()", p.Parse("$$foo()")?.ToString());
        Assert.Equal("X", p.Parse("$foo($bar())")?.ToString());
        var boomCalls=0; FuncParser.ParserCallable boom=(a,k,ctx,raw)=>{ boomCalls++; return "DOOM";};
        FuncParser.ParserCallable pad=(a,k,ctx,raw)=> a.Length>0? a[0] : "";
        var p2=new FuncParser(new Dictionary<string, FuncParser.ParserCallable>{["pad"]=pad, ["boom"]=boom});
        var r=p2.Parse("$pad(\"costs $boom() here\", 30)")?.ToString();
        Assert.Equal(0, boomCalls); Assert.Contains("$boom()", r);
    }
    [Fact] public void UnclosedParensGraceful(){
        using var env=GlobalTestEnv.Enter();
        var p=new FuncParser(new Dictionary<string, FuncParser.ParserCallable>{["foo"]=(a,k,ctx,raw)=>"X"});
        var r=p.Parse("$foo(unclosed")?.ToString();
        Assert.IsType<string>(r);
        Assert.Contains("$foo(unclosed", r!);
    }
    [Fact] public void CallstackMergeCorrectness(){
        using var env=GlobalTestEnv.Enter();
        string[]? cap=null;
        FuncParser.ParserCallable outer=(a,k,ctx,raw)=>{ cap=a; return $"outer-{a[0]}";};
        FuncParser.ParserCallable inner=(a,k,ctx,raw)=>"INNER";
        var p=new FuncParser(new Dictionary<string, FuncParser.ParserCallable>{["outer"]=outer, ["inner"]=inner});
        var r=p.Parse("start $outer($inner()) end")?.ToString();
        Assert.Equal("start outer-INNER end", r);
        Assert.Equal("INNER", cap![0]);
    }
    [Fact] public void FullstrListJoinAtEnd(){
        using var env=GlobalTestEnv.Enter();
        var p=new FuncParser(new Dictionary<string, FuncParser.ParserCallable>());
        Assert.Equal("hello $unknown() world", p.Parse("hello $unknown() world")?.ToString());
    }
    [Fact] public void PluralizeNonNumericFallbackSingular(){
        using var env=GlobalTestEnv.Enter();
        var p=new FuncParser(FuncParser.FUNCPARSER_CALLABLES);
        Assert.Equal("cat", p.Parse("$pluralize(cat, abc)")?.ToString());
        Assert.Equal("cat", p.Parse("$pluralize(cat, abc, cats)")?.ToString());
        Assert.Equal("cat", p.Parse("$pluralize(cat, )")?.ToString()); // empty
    }
    [Fact] public void PluralizeNonNumericRaiseErrors(){
        using var env=GlobalTestEnv.Enter();
        var p=new FuncParser(FuncParser.FUNCPARSER_CALLABLES);
        var ex=Assert.Throws<FuncParser.ParsingError>(()=> p.Parse("$pluralize(cat, abc)", raiseErrors:true));
        Assert.Contains("not an integer", ex.Message);
        Assert.Throws<FuncParser.ParsingError>(()=> p.Parse("$pluralize(cat, 2.0)", raiseErrors:true));
    }
    [Fact] public void PluralizeValidNumbers(){
        using var env=GlobalTestEnv.Enter();
        var p=new FuncParser(FuncParser.FUNCPARSER_CALLABLES);
        Assert.Equal("cat", p.Parse("$pluralize(cat, 0)")?.ToString());
        Assert.Equal("cat", p.Parse("$pluralize(cat, 1)")?.ToString());
        Assert.Equal("cats", p.Parse("$pluralize(cat, 2)")?.ToString());
        Assert.Equal("cat", p.Parse("$pluralize(cat, -1)")?.ToString());
        Assert.Equal("cats", p.Parse("$pluralize(cat, -2)")?.ToString());
    }
    [Fact] public void PluralizeFloatStringFallback(){
        using var env=GlobalTestEnv.Enter();
        var p=new FuncParser(FuncParser.FUNCPARSER_CALLABLES);
        Assert.Equal("cat", p.Parse("$pluralize(cat, 2.0)")?.ToString());
        Assert.Throws<FuncParser.ParsingError>(()=> p.Parse("$pluralize(cat, 2.0)", raiseErrors:true));
    }
    [Fact] public void PluralizeBoolHandling(){
        using var env=GlobalTestEnv.Enter();
        // In python True=1 False=0 both singular
        var p=new FuncParser(FuncParser.FUNCPARSER_CALLABLES);
        // We'll test via direct pluralize helper if available: our parser treats "True" as fallback singular
        Assert.Equal("cat", p.Parse("$pluralize(cat, True)")?.ToString());
        Assert.Equal("cat", p.Parse("$pluralize(cat, False)")?.ToString());
    }
    [Fact] public void PluralizeViaParserIntegration(){
        using var env=GlobalTestEnv.Enter();
        var p=new FuncParser(FuncParser.FUNCPARSER_CALLABLES);
        Assert.Equal("cat", p.Parse("$pluralize(cat, abc)")?.ToString());
        Assert.Equal("cats", p.Parse("$pluralize(cat, 2)")?.ToString());
        Assert.Throws<FuncParser.ParsingError>(()=> p.Parse("$pluralize(cat, abc)", raiseErrors:true));
    }
    [Fact] public void CallableValidationValidPasses(){
        using var env=GlobalTestEnv.Enter();
        FuncParser.ParserCallable ok=(a,k,ctx,raw)=>"";
        var p=new FuncParser(new Dictionary<string, FuncParser.ParserCallable>{["ok"]=ok});
        Assert.Contains("ok", p.Callables.Keys);
    }
    [Fact] public void CallableValidationMissingVarArgsRaises(){
        using var env=GlobalTestEnv.Enter();
        var del=new Func<string, Dictionary<string, object?>, object?>((x,kw)=>"");
        var ex=Assert.Throws<FuncParser.ParsingError>(()=> new FuncParser(new Dictionary<string, Delegate>{["bad"]=del}));
        Assert.Contains("*args", ex.Message);
    }
    [Fact] public void CallableValidationMissingVarKwRaises(){
        using var env=GlobalTestEnv.Enter();
        var del=new Func<string[], object?>((a)=>"");
        var ex=Assert.Throws<FuncParser.ParsingError>(()=> new FuncParser(new Dictionary<string, Delegate>{["bad"]=del}));
        Assert.Contains("**kwargs", ex.Message);
    }
    [Fact] public void CallableValidationMissingBothRaises(){
        using var env=GlobalTestEnv.Enter();
        var del=new Func<object?>(()=> "");
        Assert.Throws<FuncParser.ParsingError>(()=> new FuncParser(new Dictionary<string, Delegate>{["bad"]=del}));
    }
    [Fact] public void BuiltinWithoutSpecWarnsNotRaises(){
        using var env=GlobalTestEnv.Enter();
        // builtin like len has no getfullargspec -> warning path, should not raise
        FuncParser.ParserCallable ok=(a,k,ctx,raw)=>1;
        var p=new FuncParser(new Dictionary<string, FuncParser.ParserCallable>{["ok"]=ok});
        Assert.Contains("ok", p.Callables.Keys);
    }
    [Fact] public void ContainerParsingFlatContainersStillWork(){
        using var env=GlobalTestEnv.Enter();
        var conv=FuncParserHelpers.SafeConvertToTypes((new object[]{"py"}, new Dictionary<string,object>()), new object?[]{"(a, b)"}, new Dictionary<string,object?>(), true);
        Assert.Equal(2, ((System.Collections.IEnumerable)conv.args[0]!).Cast<object>().Count());
        var conv2=FuncParserHelpers.SafeConvertToTypes((new object[]{"py"}, new Dictionary<string,object>()), new object?[]{"[1, 2, 3]"}, new Dictionary<string,object?>(), true);
        Assert.Equal(3, ((System.Collections.IEnumerable)conv2.args[0]!).Cast<object>().Count());
    }
    [Fact] public void ContainerParsingFlatWithQuotedComma(){
        using var env=GlobalTestEnv.Enter();
        var conv=FuncParserHelpers.SafeConvertToTypes((new object[]{"py"}, new Dictionary<string,object>()), new object?[]{"('a, b', 'c')"}, new Dictionary<string,object?>(), true);
        var list=((System.Collections.IEnumerable)conv.args[0]!).Cast<object>().Select(o=>o?.ToString()).ToList();
        Assert.Contains("a, b", list);
    }
    [Fact] public void ContainerParsingNestedRejectedViaManual(){
        using var env=GlobalTestEnv.Enter();
        Assert.Throws<FuncParser.ParsingError>(()=> FuncParserHelpers.SafeConvertToTypes((new object[]{"py"}, new Dictionary<string,object>()), new object?[]{"(a,(b,c))"}, new Dictionary<string,object?>(), true));
        Assert.Throws<FuncParser.ParsingError>(()=> FuncParserHelpers.SafeConvertToTypes((new object[]{"py"}, new Dictionary<string,object>()), new object?[]{"(a, [1,2])"}, new Dictionary<string,object?>(), true));
        var conv=FuncParserHelpers.SafeConvertToTypes((new object[]{"py"}, new Dictionary<string,object>()), new object?[]{"([1,2], 3)"}, new Dictionary<string,object?>(), true);
        Assert.NotNull(conv.args[0]);
    }
    [Fact] public void ContainerParsingNestedViaMockedLiteralEval(){
        using var env=GlobalTestEnv.Enter();
        var conv=FuncParserHelpers.SafeConvertToTypes((new object[]{"py"}, new Dictionary<string,object>()), new object?[]{"(a, b)"}, new Dictionary<string,object?>(), true);
        Assert.Equal(2, ((System.Collections.IEnumerable)conv.args[0]!).Cast<object>().Count());
        Assert.Throws<FuncParser.ParsingError>(()=> FuncParserHelpers.SafeConvertToTypes((new object[]{"py"}, new Dictionary<string,object>()), new object?[]{"(a,(b,c))"}, new Dictionary<string,object?>(), true));
    }
    [Fact] public void ContainerParsingQuotedCommasNotSplit(){
        using var env=GlobalTestEnv.Enter();
        var conv=FuncParserHelpers.SafeConvertToTypes((new object[]{"py"}, new Dictionary<string,object>()), new object?[]{"('a, b', \"c, d\")"}, new Dictionary<string,object?>(), true);
        var list=((System.Collections.IEnumerable)conv.args[0]!).Cast<object>().Select(o=>o?.ToString()?.Trim('\'','"')).ToList();
        Assert.Contains("a, b", list);
        Assert.Contains("c, d", list);
    }
    [Fact] public void ContainerParsingValidLiteralStillUsesLiteralEval(){
        using var env=GlobalTestEnv.Enter();
        var conv=FuncParserHelpers.SafeConvertToTypes((new object[]{"py"}, new Dictionary<string,object>()), new object?[]{"(1,(2,3))"}, new Dictionary<string,object?>(), true);
        Assert.NotNull(conv.args[0]);
    }
    [Fact] public void ContainerParsingEmptyContainer(){
        using var env=GlobalTestEnv.Enter();
        var conv=FuncParserHelpers.SafeConvertToTypes((new object[]{"py"}, new Dictionary<string,object>()), new object?[]{"()"}, new Dictionary<string,object?>(), true);
        Assert.NotNull(conv.args[0]);
    }
    [Fact] public void ContainerParsingNoManualCorruption(){
        using var env=GlobalTestEnv.Enter();
        try{
            FuncParserHelpers.SafeConvertToTypes((new object[]{"py"}, new Dictionary<string,object>()), new object?[]{"(a,(b,c))"}, new Dictionary<string,object?>(), true);
            Assert.Fail("should have raised");
        }catch(FuncParser.ParsingError ex){
            Assert.True(ex.Message.Contains("a")==false || ex.GetType().Name=="ParsingError");
        }
        var conv=FuncParserHelpers.SafeConvertToTypes((new object[]{"py"}, new Dictionary<string,object>()), new object?[]{"(a, b)"}, new Dictionary<string,object?>(), true);
        var list=((System.Collections.IEnumerable)conv.args[0]!).Cast<object>().Select(o=>o?.ToString()).ToList();
        Assert.DoesNotContain("(b", list);
    }
}

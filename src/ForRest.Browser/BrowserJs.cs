namespace ForRest.Browser;

/// <summary>
/// JavaScript snippets injected into the page via <c>Runtime.evaluate</c>. These provide the engine's
/// "eyes" (locating elements, computing stable selectors, snapshotting the interactive surface),
/// element operations (read text/attributes, scroll, select), and the visible red cursor overlay.
/// Kept in one place so the protocol layer stays declarative.
/// </summary>
internal static class BrowserJs
{
    #region Internal Methods

    /// <summary>Builds an expression that locates a single element and returns its rect plus stable selectors as a JSON string.</summary>
    internal static string Locate(BrowserTarget target) =>
        $"({LocateFunction})({Args(target)})";

    /// <summary>
    /// Builds an expression that locates an element and performs an operation on it, returning a JSON
    /// string of the form <c>{ok, value}</c>. Operations: <c>text</c>, <c>attr</c>, <c>scroll</c>, <c>select</c>.
    /// </summary>
    internal static string Operate(BrowserTarget target, string operation, string argument)
    {
        string op = JsonValue.Create(operation)!.ToJsonString();
        string arg = JsonValue.Create(argument)!.ToJsonString();
        return $"({OperateFunction})({Args(target)},{op},{arg})";
    }

    /// <summary>Builds an expression that returns the page's interactive elements as a JSON string.</summary>
    internal static string Snapshot() => $"({SnapshotFunction})()";

    /// <summary>Dispatches a key on the active element. <c>Enter</c> also submits the enclosing form.</summary>
    internal static string PressKey(string keys)
    {
        string key = JsonValue.Create(keys)!.ToJsonString();
        return $"({PressFunction})({key})";
    }

    /// <summary>Moves (and lazily creates) the visible red cursor overlay to a page coordinate.</summary>
    internal static string MoveCursor(double x, double y)
    {
        string xs = x.ToString("0.##", CultureInfo.InvariantCulture);
        string ys = y.ToString("0.##", CultureInfo.InvariantCulture);
        return $"({CursorFunction})({xs},{ys},true)";
    }

    /// <summary>Hides the visible red cursor overlay.</summary>
    internal static string HideCursor() => $"({CursorFunction})(0,0,false)";

    /// <summary>
    /// Builds a self-running script that pre-creates the hidden cursor overlay as soon as the document
    /// is ready. Register it with <c>Page.addScriptToEvaluateOnNewDocument</c> (so it re-installs after
    /// every navigation, which wipes injected DOM) and evaluate it once for the current document. The
    /// per-move <see cref="MoveCursor"/> call still self-heals if the overlay is missing.
    /// </summary>
    internal static string InstallCursor() =>
        $"(function(){{var f=({CursorFunction});function b(){{try{{f(-100,-100,false);}}catch(e){{}}}}if(document.body){{b();}}else if(document.addEventListener){{document.addEventListener('DOMContentLoaded',b);}}}})()";

    /// <summary>Plays a brief click ripple at a page coordinate so taps read clearly to a watching human.</summary>
    internal static string ClickRipple(double x, double y)
    {
        string xs = x.ToString("0.##", CultureInfo.InvariantCulture);
        string ys = y.ToString("0.##", CultureInfo.InvariantCulture);
        return $"({RippleFunction})({xs},{ys})";
    }

    #endregion

    #region Private Methods

    private static string Args(BrowserTarget target)
    {
        string kind = JsonValue.Create(target.Kind.ToString().ToLowerInvariant())!.ToJsonString();
        string value = JsonValue.Create(target.Value)!.ToJsonString();
        string name = JsonValue.Create(target.Name ?? string.Empty)!.ToJsonString();
        return $"{kind},{value},{name}";
    }

    #endregion

    #region Private Fields

    private const string Helpers =
        """
        function __frCss(el){
          if(el.id){return '#'+CSS.escape(el.id);}
          var parts=[];
          while(el&&el.nodeType===1&&parts.length<6){
            var sel=el.tagName.toLowerCase();
            if(el.classList&&el.classList.length){sel+='.'+[].slice.call(el.classList).map(function(c){return CSS.escape(c);}).join('.');}
            var parent=el.parentNode;
            if(parent){
              var sibs=[].filter.call(parent.children,function(c){return c.tagName===el.tagName;});
              if(sibs.length>1){sel+=':nth-of-type('+(sibs.indexOf(el)+1)+')';}
            }
            parts.unshift(sel);
            el=el.parentElement;
          }
          return parts.join(' > ');
        }
        function __frXp(el){
          if(el.id){return '//*[@id=\"'+el.id+'\"]';}
          var parts=[];
          while(el&&el.nodeType===1){
            var ix=1,sib=el.previousElementSibling;
            while(sib){if(sib.tagName===el.tagName){ix++;}sib=sib.previousElementSibling;}
            parts.unshift(el.tagName.toLowerCase()+'['+ix+']');
            el=el.parentElement;
          }
          return '/'+parts.join('/');
        }
        function __frRole(el){return el.getAttribute('role')||({a:'link',button:'button',input:'textbox',select:'combobox',textarea:'textbox'})[el.tagName.toLowerCase()]||'';}
        function __frName(el){return (el.getAttribute('aria-label')||el.getAttribute('placeholder')||(el.textContent||'').trim()).slice(0,160);}
        function __frFind(kind,value,name){
          if(kind==='css'){return document.querySelector(value);}
          if(kind==='xpath'){var r=document.evaluate(value,document,null,XPathResult.FIRST_ORDERED_NODE_TYPE,null);return r.singleNodeValue;}
          if(kind==='testid'){return document.querySelector('[data-testid='+CSS.escape(value)+'],[data-test-id='+CSS.escape(value)+']');}
          if(kind==='text'){return [].slice.call(document.querySelectorAll('body *')).filter(function(e){return e.children.length===0&&(e.textContent||'').trim()===value;})[0]||null;}
          if(kind==='role'){return [].slice.call(document.querySelectorAll('*')).filter(function(e){if(__frRole(e)!==value){return false;}return !name||__frName(e)===name;})[0]||null;}
          return null;
        }
        function __frInfo(el){
          var b=el.getBoundingClientRect();
          return {found:true,x:b.left+b.width/2,y:b.top+b.height/2,width:b.width,height:b.height,tag:el.tagName.toLowerCase(),id:el.id||'',text:(el.textContent||'').trim().slice(0,200),css:__frCss(el),xpath:__frXp(el),role:__frRole(el),name:__frName(el)};
        }
        """;

    private static readonly string LocateFunction =
        Helpers +
        """
        ;function(kind,value,name){
          var el=null;
          try{el=__frFind(kind,value,name);}catch(e){return JSON.stringify({found:false,error:String(e)});}
          if(!el){return JSON.stringify({found:false});}
          el.scrollIntoView({block:'center',inline:'center'});
          return JSON.stringify(__frInfo(el));
        }
        """;

    private static readonly string OperateFunction =
        Helpers +
        """
        ;function(kind,value,name,op,arg){
          var el=null;
          try{el=__frFind(kind,value,name);}catch(e){return JSON.stringify({ok:false,error:String(e)});}
          if(!el){return JSON.stringify({ok:false});}
          var out=null;
          if(op==='text'){out=(el.textContent||'').trim();}
          else if(op==='attr'){out=el.getAttribute(arg);}
          else if(op==='scroll'){el.scrollIntoView({block:'center',inline:'center'});}
          else if(op==='select'){el.value=arg;el.dispatchEvent(new Event('input',{bubbles:true}));el.dispatchEvent(new Event('change',{bubbles:true}));}
          else if(op==='hover'){el.scrollIntoView({block:'center',inline:'center'});['mouseover','mousemove','mouseenter'].forEach(function(t){el.dispatchEvent(new MouseEvent(t,{bubbles:true}));});}
          else if(op==='click'){
            el.scrollIntoView({block:'center',inline:'center'});
            if(el.focus){try{el.focus();}catch(e){}}
            ['mousedown','mouseup','click'].forEach(function(t){el.dispatchEvent(new MouseEvent(t,{bubbles:true,cancelable:true,view:window}));});
          }
          else if(op==='type'){
            if(el.focus){try{el.focus();}catch(e){}}
            if(el.isContentEditable){el.textContent=arg;}
            else{el.value=arg;}
            el.dispatchEvent(new Event('input',{bubbles:true}));
            el.dispatchEvent(new Event('change',{bubbles:true}));
          }
          return JSON.stringify({ok:true,value:out});
        }
        """;

    private static readonly string SnapshotFunction =
        Helpers +
        """
        ;function(){
          var nodes=[].slice.call(document.querySelectorAll('a,button,input,select,textarea,[role],[onclick],[contenteditable=true]'));
          var out=[];
          for(var i=0;i<nodes.length&&out.length<200;i++){
            var el=nodes[i];var b=el.getBoundingClientRect();
            if(b.width===0&&b.height===0){continue;}
            out.push(__frInfo(el));
          }
          return JSON.stringify({url:location.href,title:document.title,elements:out});
        }
        """;

    private const string CursorFunction =
        """
        function(x,y,show){
          var id='__forrest_cursor__';
          if(!document.body){return false;}
          var c=document.getElementById(id);
          if(!c){
            c=document.createElement('div');c.id=id;
            c.style.cssText='position:fixed;z-index:2147483647;left:0;top:0;width:22px;height:30px;pointer-events:none;opacity:0;will-change:transform;transition:transform 70ms cubic-bezier(0.22,0.61,0.36,1),opacity 120ms ease;filter:drop-shadow(0 2px 3px rgba(0,0,0,0.45));transform:translate(-100px,-100px);';
            c.innerHTML='<svg width="22" height="30" viewBox="0 0 22 30" xmlns="http://www.w3.org/2000/svg"><path d="M1 1 L1 22 L6.5 16.5 L10 25 L13 23.7 L9.6 15.4 L17 15 Z" fill="#dc3545" stroke="#ffffff" stroke-width="1.4" stroke-linejoin="round"/></svg>';
            document.body.appendChild(c);
          }
          if(show){
            c.style.opacity='1';
            // Tip of the arrow sits at the requested coordinate (svg hotspot is at its top-left).
            c.style.transform='translate('+x+'px,'+y+'px)';
          }else{
            c.style.opacity='0';
          }
          return true;
        }
        """;

    private const string PressFunction =
        """
        function(key){
          var el=document.activeElement||document.body;
          var opts={bubbles:true,cancelable:true,key:key};
          ['keydown','keypress','keyup'].forEach(function(t){el.dispatchEvent(new KeyboardEvent(t,opts));});
          if(key==='Enter'){
            if(el.form){try{if(el.form.requestSubmit){el.form.requestSubmit();}else{el.form.submit();}}catch(e){}}
            else if(el.tagName&&el.tagName.toLowerCase()==='a'&&el.click){el.click();}
          }
          return JSON.stringify({ok:true});
        }
        """;

    private const string RippleFunction =
        """
        function(x,y){
          var r=document.createElement('div');
          r.style.cssText='position:fixed;z-index:2147483646;left:'+(x-7)+'px;top:'+(y-7)+'px;width:14px;height:14px;border-radius:50%;border:2px solid rgba(220,53,69,0.9);background:rgba(220,53,69,0.18);pointer-events:none;';
          document.body.appendChild(r);
          var anim=r.animate([{transform:'scale(0.4)',opacity:0.9},{transform:'scale(2.6)',opacity:0}],{duration:420,easing:'ease-out'});
          anim.onfinish=function(){if(r&&r.parentNode){r.parentNode.removeChild(r);}};
          return true;
        }
        """;

    #endregion
}

namespace ForRest.Browser;

/// <summary>
/// JavaScript injected into the live page while the browser pane is in record mode. It listens for the
/// user's clicks, text input, and select changes, computes a stable selector for each target (preferring
/// <c>id</c>, then a unique CSS path, then XPath — mirroring the locator helpers in <see cref="BrowserJs"/>),
/// and posts a compact JSON event to the host over the WebView's JS-to-host message channel. The host
/// translates those events into <c>browser.*</c> script lines. Kept here so the selector logic lives next
/// to the rest of the engine's "eyes" and stays engine-agnostic; the host owns the channel wiring.
/// </summary>
public static class BrowserRecorderJs
{
    #region Public Methods

    /// <summary>
    /// Builds the recorder bootstrap. <paramref name="postExpression"/> is a JavaScript snippet that
    /// delivers a single string argument named <c>__frPayload</c> to the host (for example
    /// <c>window.chrome.webview.postMessage(__frPayload)</c> on WebView2). The recorder is idempotent: it
    /// installs listeners once per document and tears them down when <c>__frStopRecording</c> is called.
    /// </summary>
    public static string Build(string postExpression) =>
        Helpers +
        ";(function(){\n" +
        "  if(window.__frRecorderInstalled){return;}\n" +
        "  window.__frRecorderInstalled=true;\n" +
        "  function __frPost(__frPayload){try{" + postExpression + ";}catch(e){}}\n" +
        "  function __frSelector(el){\n" +
        "    if(!el||el.nodeType!==1){return null;}\n" +
        "    if(el.id){return {kind:'css',value:'#'+CSS.escape(el.id)};}\n" +
        "    var css=__frCss(el);\n" +
        "    try{if(css&&document.querySelectorAll(css).length===1){return {kind:'css',value:css};}}catch(e){}\n" +
        "    return {kind:'xpath',value:__frXp(el)};\n" +
        "  }\n" +
        "  function __frEmit(type,el,extra){\n" +
        "    var sel=__frSelector(el);\n" +
        "    if(!sel){return;}\n" +
        "    var payload={type:type,selectorKind:sel.kind,selector:sel.value};\n" +
        "    if(extra){for(var k in extra){payload[k]=extra[k];}}\n" +
        "    __frPost(JSON.stringify(payload));\n" +
        "  }\n" +
        "  function __frClick(e){\n" +
        "    var el=e.target;\n" +
        "    if(!el){return;}\n" +
        "    var tag=el.tagName?el.tagName.toLowerCase():'';\n" +
        "    if(tag==='input'){var t=(el.getAttribute('type')||'').toLowerCase();if(t==='text'||t==='password'||t==='email'||t==='search'||t==='url'||t==='tel'||t==='number'){return;}}\n" +
        "    if(tag==='textarea'||tag==='select'){return;}\n" +
        "    __frEmit('click',el,null);\n" +
        "  }\n" +
        "  function __frChange(e){\n" +
        "    var el=e.target;\n" +
        "    if(!el){return;}\n" +
        "    var tag=el.tagName?el.tagName.toLowerCase():'';\n" +
        "    if(tag==='select'){__frEmit('select',el,{value:el.value});return;}\n" +
        "    if(tag==='input'||tag==='textarea'){__frEmit('type',el,{value:el.value});}\n" +
        "  }\n" +
        "  window.__frStopRecording=function(){\n" +
        "    document.removeEventListener('click',__frClick,true);\n" +
        "    document.removeEventListener('change',__frChange,true);\n" +
        "    window.__frRecorderInstalled=false;\n" +
        "    return true;\n" +
        "  };\n" +
        "  document.addEventListener('click',__frClick,true);\n" +
        "  document.addEventListener('change',__frChange,true);\n" +
        "})();";

    /// <summary>Expression that disables an already-installed recorder in the current document.</summary>
    public const string Stop = "(window.__frStopRecording?window.__frStopRecording():false)";

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
        """;

    #endregion
}

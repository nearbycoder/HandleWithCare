// The browser build's few calls out to the page (Assets/Scripts/Game/WebPlatform.cs).
mergeInto(LibraryManager.library, {
  // a photo or a GIF: the browser saves it to its downloads
  HWC_Download: function (name, mime, data, length) {
    var bytes = HEAPU8.slice(data, data + length);
    var blob = new Blob([bytes], { type: UTF8ToString(mime) });
    var url = URL.createObjectURL(blob);
    var a = document.createElement("a");
    a.href = url;
    a.download = UTF8ToString(name);
    a.style.display = "none";
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    setTimeout(function () { URL.revokeObjectURL(url); }, 10000);
  },

  // the title screen is up: the page drops its loading cover
  HWC_Ready: function () {
    if (typeof window.hwcReady === "function") window.hwcReady();
  },

  // the touch controls follow the game (Assets/Scripts/Game/TouchInput.cs): a JSON object
  HWC_TouchState: function (json) {
    if (typeof window.hwcTouchState === "function") window.hwcTouchState(UTF8ToString(json));
  },

  // the game switched input device by itself ("pad": a gamepad took over)
  HWC_InputMode: function (mode) {
    if (typeof window.hwcInputMode === "function") window.hwcInputMode(UTF8ToString(mode));
  },
});

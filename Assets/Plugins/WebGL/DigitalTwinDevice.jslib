mergeInto(LibraryManager.library, {
  DigitalTwin_IsMobileBrowser: function () {
    var ua = navigator.userAgent || navigator.vendor || window.opera || "";
    var mobileUa = /android|iphone|ipad|ipod|mobile|silk|kindle/i.test(ua);
    var coarsePointer = window.matchMedia && window.matchMedia("(pointer: coarse)").matches;
    var touchPoints = navigator.maxTouchPoints || 0;
    return mobileUa || (coarsePointer && touchPoints > 0) ? 1 : 0;
  },

  // Minutes to add to local time to get UTC at that moment (JavaScript's sign),
  // so daylight saving is right for every point on a year-long chart.
  DigitalTwin_GetTimezoneOffsetMinutes: function (unixMilliseconds) {
    return new Date(unixMilliseconds).getTimezoneOffset();
  },

  // The AI data table: kept on window.DigitalTwinAi.latest and announced as a
  // window event, so the page can forward it to a model without polling.
  DigitalTwin_PublishAiTable: function (eventNamePtr, jsonPtr) {
    var eventName = UTF8ToString(eventNamePtr);
    var json = UTF8ToString(jsonPtr);
    try {
      window.DigitalTwinAi = window.DigitalTwinAi || {};
      window.DigitalTwinAi.latestJson = json;
      window.dispatchEvent(new CustomEvent(eventName, { detail: json }));
    } catch (error) {
      console.warn("Digital twin: could not publish the AI data table", error);
    }
  },

  // Wheel and trackpad scrolling for the lists, in the canvas's own pixels,
  // exactly as the browser reports it (see UIWheelScroll). Pinch-zoom
  // gestures arrive as wheel events with ctrlKey and are left alone.
  DigitalTwin_StartWheelCapture: function () {
    if (window.__digitalTwinWheel)
      return;
    var state = { y: 0 };
    window.__digitalTwinWheel = state;
    var canvas = (typeof Module !== "undefined" && Module.canvas) ||
                 document.querySelector("#unity-canvas") || document.querySelector("canvas");
    (canvas || window).addEventListener("wheel", function (event) {
      if (event.ctrlKey)
        return;
      var cssPixels = event.deltaY;
      if (event.deltaMode === 1)
        cssPixels *= 16;                        // lines
      else if (event.deltaMode === 2)
        cssPixels *= (window.innerHeight || 800); // pages
      var toCanvas = canvas && canvas.clientHeight > 0 ? canvas.height / canvas.clientHeight : (window.devicePixelRatio || 1);
      state.y += cssPixels * toCanvas;
    }, { passive: true, capture: true });
  },

  DigitalTwin_TakeWheelDeltaY: function () {
    var state = window.__digitalTwinWheel;
    if (!state)
      return 0;
    var y = state.y;
    state.y = 0;
    return y;
  },

  DigitalTwin_SaveText: function (fileNamePtr, mimeTypePtr, textPtr) {
    var fileName = UTF8ToString(fileNamePtr);
    var mimeType = UTF8ToString(mimeTypePtr);
    var text = UTF8ToString(textPtr);
    try {
      var blob = new Blob([text], { type: mimeType + ";charset=utf-8" });
      var url = URL.createObjectURL(blob);
      var link = document.createElement("a");
      link.href = url;
      link.download = fileName;
      link.style.display = "none";
      document.body.appendChild(link);
      link.click();
      document.body.removeChild(link);
      setTimeout(function () { URL.revokeObjectURL(url); }, 1000);
    } catch (error) {
      console.warn("Digital twin: could not save " + fileName, error);
    }
  }
});

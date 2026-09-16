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
  }
});

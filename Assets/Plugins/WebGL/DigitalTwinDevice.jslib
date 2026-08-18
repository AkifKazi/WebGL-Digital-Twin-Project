mergeInto(LibraryManager.library, {
  DigitalTwin_IsMobileBrowser: function () {
    var ua = navigator.userAgent || navigator.vendor || window.opera || "";
    var mobileUa = /android|iphone|ipad|ipod|mobile|silk|kindle/i.test(ua);
    var coarsePointer = window.matchMedia && window.matchMedia("(pointer: coarse)").matches;
    var touchPoints = navigator.maxTouchPoints || 0;
    return mobileUa || (coarsePointer && touchPoints > 0) ? 1 : 0;
  }
});

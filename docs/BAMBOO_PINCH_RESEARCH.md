# Continuous pinch investigation (2026-10-15)

Status: researched, not implemented as a native systemwide gesture.

Apple AppKit exposes receiving magnification gestures and a read-only `NSEvent.magnification`. The public macOS 27 SDK has no documented CGEvent constructor/field for a magnification event with a writable magnitude. The inspected NSEvent factories cover mouse, key, tracking and custom application events; custom events are not a documented way to manufacture trackpad gestures for other applications.

Sources:
- https://developer.apple.com/documentation/appkit/nsevent/magnification
- https://developer.apple.com/documentation/appkit/nsresponder/magnify(with:)
- https://developer.apple.com/documentation/appkit/nsevent

Conclusion from those APIs: a reliable general-purpose native pinch cannot currently be promised by this implementation using documented event APIs. Undocumented event fields or a virtual multitouch device would be a separate experimental backend, requiring cross-application and OS-update validation. They are not silently enabled in this preview.

The existing pinch sends Command-plus/minus, with rate limiting. It remains explicitly labelled as keyboard-shortcut zoom in the UI and can be disabled. This is stepped application zoom, not smooth trackpad magnification. Browser and ChatGPT behavior was physically compared earlier; no new native smooth pinch was validated in either. Preview native magnification also remains unvalidated.

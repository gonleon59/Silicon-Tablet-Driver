import AppKit
let image = NSImage(size: NSSize(width: 48, height: 48))
image.lockFocus()
NSColor.white.setStroke()
NSColor.white.setFill()
let tablet = NSBezierPath(roundedRect: NSRect(x: 4, y: 10, width: 34, height: 27), xRadius: 4, yRadius: 4)
tablet.lineWidth = 2.5
tablet.stroke()
let area = NSBezierPath(roundedRect: NSRect(x: 11, y: 15, width: 22, height: 17), xRadius: 2, yRadius: 2)
area.lineWidth = 1.5
area.stroke()
let pen = NSBezierPath()
pen.move(to: NSPoint(x: 37, y: 12)); pen.line(to: NSPoint(x: 44, y: 35))
pen.lineWidth = 2.5; pen.stroke()
for y in [19, 26] { NSBezierPath(ovalIn: NSRect(x: 6, y: y, width: 2, height: 2)).fill() }
image.unlockFocus()
guard let tiff = image.tiffRepresentation, let bitmap = NSBitmapImageRep(data: tiff),
      let png = bitmap.representation(using: .png, properties: [:]) else { fatalError("Cannot render icon") }
try png.write(to: URL(fileURLWithPath: CommandLine.arguments[1]))

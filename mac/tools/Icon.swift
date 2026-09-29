import AppKit
import Foundation

let directory = CommandLine.arguments[1]
try FileManager.default.createDirectory(atPath: directory, withIntermediateDirectories: true)
for size in [16, 32, 128, 256, 512] {
    for scale in [1, 2] {
        let pixels = size * scale
        let bitmap = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: pixels, pixelsHigh: pixels, bitsPerSample: 8, samplesPerPixel: 4, hasAlpha: true, isPlanar: false, colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0)!
        NSGraphicsContext.saveGraphicsState(); NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: bitmap)
        let p = CGFloat(pixels)
        NSColor(calibratedWhite: 0.10, alpha: 1).setFill()
        NSBezierPath(roundedRect: NSRect(x: p * 0.05, y: p * 0.05, width: p * 0.90, height: p * 0.90), xRadius: p * 0.20, yRadius: p * 0.20).fill()
        NSColor(calibratedRed: 0.91, green: 0.79, blue: 0.71, alpha: 1).setStroke()
        let check = NSBezierPath(); check.lineWidth = p * 0.065; check.lineCapStyle = .round; check.lineJoinStyle = .round
        check.move(to: NSPoint(x: p * 0.28, y: p * 0.49)); check.line(to: NSPoint(x: p * 0.43, y: p * 0.34)); check.line(to: NSPoint(x: p * 0.73, y: p * 0.66)); check.stroke()
        NSGraphicsContext.restoreGraphicsState()
        let name = "icon_\(size)x\(size)\(scale == 2 ? "@2x" : "").png"
        try bitmap.representation(using: .png, properties: [:])!.write(to: URL(fileURLWithPath: directory).appendingPathComponent(name))
    }
}

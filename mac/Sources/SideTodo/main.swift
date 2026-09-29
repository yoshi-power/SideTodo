import AppKit
import Foundation

if CommandLine.arguments.contains("--self-test") {
    do { try SelfTests.run(); exit(0) }
    catch { fputs("FAIL: \(error)\n", stderr); exit(1) }
}

let app = NSApplication.shared
// Prevent two separately launched copies from writing the same store.
if let identifier = Bundle.main.bundleIdentifier,
   NSRunningApplication.runningApplications(withBundleIdentifier: identifier).contains(where: { $0.processIdentifier != ProcessInfo.processInfo.processIdentifier }) {
    exit(0)
}
let delegate = AppDelegate()
app.delegate = delegate
app.run()

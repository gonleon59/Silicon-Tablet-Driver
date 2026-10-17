import AppKit
import IOKit

// IOKit notifies once per USB device, rather than once per HID interface.
final class TabletConnectionMonitor {
    private let applicationURL: URL
    private let bundleIdentifier: String
    private let interfaceExecutable: URL?
    private var notificationPort: IONotificationPortRef?
    private var iterator: io_iterator_t = 0
    private var launchInFlight = false

    init(applicationURL: URL) {
        self.applicationURL = applicationURL
        let bundle = Bundle(url: applicationURL)
        self.bundleIdentifier = bundle?.bundleIdentifier ?? "net.opentabletdriver"
        self.interfaceExecutable = bundle?.executableURL?.standardizedFileURL
    }

    func start() throws {
        guard let port = IONotificationPortCreate(kIOMainPortDefault),
              let matching = IOServiceMatching("IOUSBHostDevice") else {
            throw NSError(domain: "TabletConnectionMonitor", code: 1)
        }
        notificationPort = port
        IONotificationPortSetDispatchQueue(port, DispatchQueue.main)
        let context = Unmanaged.passUnretained(self).toOpaque()
        let status = IOServiceAddMatchingNotification(port, kIOFirstMatchNotification, matching, { context, iterator in
            guard let context else { return }
            Unmanaged<TabletConnectionMonitor>.fromOpaque(context).takeUnretainedValue().drain(iterator)
        }, context, &iterator)
        guard status == KERN_SUCCESS else { throw NSError(domain: "TabletConnectionMonitor", code: Int(status)) }
        drain(iterator) // Includes a tablet already connected at login.
    }

    private func drain(_ iterator: io_iterator_t) {
        var found = false
        while true {
            let device = IOIteratorNext(iterator)
            if device == 0 { break }
            defer { IOObjectRelease(device) }
            let vendor = IORegistryEntryCreateCFProperty(device, "idVendor" as CFString, kCFAllocatorDefault, 0)?.takeRetainedValue() as? NSNumber
            let product = IORegistryEntryCreateCFProperty(device, "idProduct" as CFString, kCFAllocatorDefault, 0)?.takeRetainedValue() as? NSNumber
            if vendor?.intValue == 0x056a && [0x00d3, 0x00d8, 0x00db].contains(product?.intValue ?? -1) { found = true }
        }
        guard found, !launchInFlight, FileManager.default.fileExists(atPath: applicationURL.path),
              !NSRunningApplication.runningApplications(withBundleIdentifier: bundleIdentifier).contains(where: {
                  !$0.isTerminated && $0.executableURL?.standardizedFileURL == interfaceExecutable
              }) else { return }
        launchInFlight = true
        let configuration = NSWorkspace.OpenConfiguration()
        configuration.activates = false
        configuration.arguments = ["--minimized", "--skipupdate"]
        NSWorkspace.shared.openApplication(at: applicationURL, configuration: configuration) { _, error in
            DispatchQueue.main.async { self.launchInFlight = false }
            if let error { NSLog("Tablet launch failed: %@", error.localizedDescription) }
        }
    }

    deinit {
        if iterator != 0 { IOObjectRelease(iterator) }
        if let notificationPort { IONotificationPortDestroy(notificationPort) }
    }
}

let executable = URL(fileURLWithPath: CommandLine.arguments[0]).resolvingSymlinksInPath()
let applicationURL = executable.deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
let monitor = TabletConnectionMonitor(applicationURL: applicationURL)
do {
    try monitor.start()
    RunLoop.main.run()
} catch {
    NSLog("Tablet connection monitor failed: %@", error.localizedDescription)
    exit(1)
}

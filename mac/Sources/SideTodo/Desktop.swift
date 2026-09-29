import AppKit
import SwiftUI
import UniformTypeIdentifiers
import QuartzCore

final class FloatingPanel: NSPanel {
    private var fadeTimer: Timer?
    func fade(visible: Bool, completion: (() -> Void)? = nil) {
        fadeTimer?.invalidate(); fadeTimer = nil
        ignoresMouseEvents = !visible
        if visible && !isVisible { alphaValue = 0; orderFrontRegardless() }
        let from = alphaValue, to: CGFloat = visible ? 1 : 0
        let duration = NSWorkspace.shared.accessibilityDisplayShouldReduceMotion ? 0 : (visible ? 0.24 : 0.30)
        if duration == 0 {
            alphaValue = to; if !visible { orderOut(nil) }; completion?(); return
        }
        let start = ProcessInfo.processInfo.systemUptime
        let timer = Timer(timeInterval: 1 / 60.0, repeats: true) { [self] timer in
            let progress = min(1, (ProcessInfo.processInfo.systemUptime - start) / duration)
            let eased = progress * progress * (3 - 2 * progress)
            alphaValue = from + (to - from) * eased
            if progress >= 1 {
                timer.invalidate(); fadeTimer = nil
                if !visible { orderOut(nil) }
                completion?()
            }
        }
        fadeTimer = timer; RunLoop.main.add(timer, forMode: .common)
    }
    var acceptsKeyboard = false
    override var canBecomeKey: Bool { acceptsKeyboard }
    override var canBecomeMain: Bool { false }
    var escape: (() -> Void)?
    override func cancelOperation(_ sender: Any?) { escape?() }
    override func sendEvent(_ event: NSEvent) {
        if event.type == .leftMouseDown || event.type == .rightMouseDown {
            acceptsKeyboard = true; makeKey()
        }
        super.sendEvent(event)
    }
    func showForEditing() { acceptsKeyboard = true; makeKeyAndOrderFront(nil) }
}

final class DesktopController: NSObject, NSWindowDelegate {
    let store: TaskStore
    let widget: FloatingPanel
    let marker: FloatingPanel
    var detail: FloatingPanel?
    var archive: FloatingPanel?
    var status: NSStatusItem!
    var timer: Timer?
    var typing = false
    var quickDraft = UserDefaults.standard.string(forKey: "quickDraft") ?? "" {
        didSet { UserDefaults.standard.set(quickDraft, forKey: "quickDraft") }
    }
    var selectedToday = true
    var expanded = false
    var detailPinned = false
    var detailID: UUID?
    var flushDetail: (() -> Bool)?
    var paused = false
    private var leaveTime: Date?
    private var detailLeave: Date?
    private var hoverSuppressed = false
    private var detailMoved = false
    private var positioning = false
    private var lastScreenFrame = NSRect.zero
    private var widgetSize = NSSize(width: 252, height: 130)
    private var displayID: String? { UserDefaults.standard.string(forKey: "display") }
    var screen: NSScreen {
        NSScreen.screens.first(where: { screenID($0) == displayID }) ?? NSScreen.screens[0]
    }
    var available: NSRect { screen.visibleFrame }
    var fullScreen: Bool {
        get { UserDefaults.standard.object(forKey: "fullScreen") as? Bool ?? true }
        set { UserDefaults.standard.set(newValue, forKey: "fullScreen") }
    }
    var anchor: NSPoint {
        NSPoint(x: available.minX + 3, y: available.maxY - min(160, available.height * 0.25))
    }
    init(store: TaskStore) {
        self.store = store; widget = Self.panel(); marker = Self.panel()
        super.init()
        widget.escape = { [weak self] in self?.collapse(force: true) }
        configure(widget)
        configure(marker)
        marker.hasShadow = false
        marker.contentView = NSHostingView(rootView:
            Capsule().fill(Color(white: 0.10).opacity(0.85))
                .overlay(Capsule().fill(Color(white: 0.95)).padding(3))
                .overlay(Capsule().stroke(Color.white.opacity(0.45), lineWidth: 1))
                .frame(width: 13, height: 42).padding(3))
        makeStatus()
        collapse(force: true)
        NotificationCenter.default.addObserver(self, selector: #selector(screensChanged), name: NSApplication.didChangeScreenParametersNotification, object: nil)
        NSWorkspace.shared.notificationCenter.addObserver(self, selector: #selector(wake), name: NSWorkspace.didWakeNotification, object: nil)
        timer = Timer(timeInterval: 0.1, repeats: true) { [weak self] _ in self?.tick() }
        RunLoop.main.add(timer!, forMode: .common)
    }
    static func panel() -> FloatingPanel {
        let panel = FloatingPanel(contentRect: NSRect(x: 0, y: 0, width: 252, height: 130), styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: false)
        panel.isOpaque = false; panel.backgroundColor = .clear; panel.hasShadow = true
        panel.hidesOnDeactivate = false; panel.isReleasedWhenClosed = false
        panel.isFloatingPanel = true; panel.level = .floating
        panel.becomesKeyOnlyIfNeeded = false
        panel.animationBehavior = .none
        return panel
    }
    func configure(_ panel: FloatingPanel) {
        panel.collectionBehavior = fullScreen ? [.canJoinAllSpaces, .fullScreenAuxiliary, .ignoresCycle] : [.canJoinAllSpaces, .ignoresCycle]
    }
    func screenID(_ screen: NSScreen) -> String {
        String(describing: screen.deviceDescription[NSDeviceDescriptionKey("NSScreenNumber")] ?? "")
    }
    func makeStatus() {
        status = NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)
        status.button?.image = NSImage(systemSymbolName: "checkmark.square", accessibilityDescription: "SideTodo")
        status.button?.image?.isTemplate = true
        status.button?.toolTip = "SideTodo"
        rebuildMenu()
    }
    func rebuildMenu() {
        let menu = NSMenu()
        func item(_ title: String, _ action: Selector, key: String = "") -> NSMenuItem {
            let item = NSMenuItem(title: title, action: action, keyEquivalent: key); item.target = self; menu.addItem(item); return item
        }
        _ = item("할 일 열기", #selector(openFromMenu), key: "o")
        _ = item("새 일정", #selector(newFromMenu), key: "n")
        _ = item("완료 기록", #selector(archiveFromMenu))
        menu.addItem(.separator())
        let displays = NSMenuItem(title: "위젯 모니터", action: nil, keyEquivalent: "")
        let submenu = NSMenu()
        for s in NSScreen.screens {
            let entry = NSMenuItem(title: s.localizedName, action: #selector(selectDisplay(_:)), keyEquivalent: "")
            entry.target = self; entry.representedObject = screenID(s)
            entry.state = s == screen ? .on : .off; submenu.addItem(entry)
        }
        displays.submenu = submenu; menu.addItem(displays)
        let full = item("전체 화면에서도 표시", #selector(toggleFullScreen)); full.state = fullScreen ? .on : .off
        let pause = item("가장자리 호버 일시 중지", #selector(togglePause)); pause.state = paused ? .on : .off
        menu.addItem(.separator())
        _ = item("Windows / Mac 데이터 가져오기…", #selector(importTasks))
        _ = item("데이터 폴더 열기", #selector(openData))
        _ = item("SideTodo 정보", #selector(about))
        menu.addItem(.separator())
        _ = item("SideTodo 종료", #selector(quit), key: "q")
        status.menu = menu
    }
    @objc func openFromMenu() { expand(); widget.showForEditing() }
    @objc func newFromMenu() { openDetail(nil, hover: false) }
    @objc func archiveFromMenu() { showArchive() }
    @objc func selectDisplay(_ sender: NSMenuItem) {
        UserDefaults.standard.set(sender.representedObject as? String, forKey: "display")
        screensChanged()
    }
    @objc func toggleFullScreen() { fullScreen.toggle(); for p in [widget, marker, detail, archive].compactMap({ $0 }) { configure(p) }; rebuildMenu() }
    @objc func togglePause() { paused.toggle(); collapse(force: true); rebuildMenu() }
    @objc func openData() {
        try? FileManager.default.createDirectory(at: store.url.deletingLastPathComponent(), withIntermediateDirectories: true)
        NSWorkspace.shared.open(store.url.deletingLastPathComponent())
    }
    @objc func about() {
        NSApp.activate(ignoringOtherApps: true)
        NSApp.orderFrontStandardAboutPanel(options: [.applicationName: "SideTodo", .applicationVersion: "1.1.0 · Mac Preview 1", .credits: NSAttributedString(string: "작게 열고, 가볍게 기록하세요.\n데이터는 이 Mac에만 저장됩니다.\nhttps://github.com/yoshi-power/SideTodo")])
    }
    @objc func quit() { NSApp.terminate(nil) }
    @objc func wake() { screensChanged() }
    @objc func screensChanged() {
        guard !NSScreen.screens.isEmpty else { return }
        lastScreenFrame = available; positionWidget(animated: false)
        for panel in [detail, archive].compactMap({ $0 }) { panel.setFrame(clamp(panel.frame), display: true) }
        rebuildMenu()
    }
    func tick() {
        guard !NSScreen.screens.isEmpty else { return }
        if available != lastScreenFrame { screensChanged() }
        let mouse = NSEvent.mouseLocation
        let zone = NSRect(x: anchor.x - 3, y: anchor.y - 54, width: 34, height: 108)
        if !zone.contains(mouse) && !expanded { hoverSuppressed = false }
        if !paused && !expanded && !hoverSuppressed && zone.contains(mouse) { expand() }
        if let detail, detail.isVisible, !detailPinned {
            // The union bridges the narrow gap between the list and hover card.
            let bridge = widget.frame.union(detail.frame).insetBy(dx: -12, dy: -18)
            if bridge.contains(mouse) || detail.isKeyWindow { detailLeave = nil }
            else if let since = detailLeave, Date().timeIntervalSince(since) > 0.6 { closeDetail() }
            else if detailLeave == nil { detailLeave = Date() }
        }
        guard expanded else { return }
        let inWidget = widget.frame.insetBy(dx: -12, dy: -16).contains(mouse)
        let inDetail = detail.map { $0.isVisible && $0.frame.insetBy(dx: -16, dy: -16).contains(mouse) } ?? false
        // A focused input is kept open, but clicking another app releases it naturally.
        let editing = typing && widget.isKeyWindow
        if inWidget || inDetail || editing || detailPinned { leaveTime = nil }
        else if let since = leaveTime, Date().timeIntervalSince(since) > 0.65 { collapse() }
        else if leaveTime == nil { leaveTime = Date() }
    }
    func expand() {
        guard !expanded else { return }
        expanded = true; leaveTime = nil
        widget.acceptsKeyboard = false
        if widget.contentView == nil { widget.contentView = NSHostingView(rootView: WidgetView(store: store, controller: self)) }
        positionWidget(animated: false)
        marker.fade(visible: false); widget.fade(visible: true)
    }
    func collapse(force: Bool = false) {
        if !force && (detailPinned || (typing && widget.isKeyWindow)) { return }
        expanded = false; typing = false; leaveTime = nil; hoverSuppressed = true
        if widget.isKeyWindow { widget.resignKey() }
        widget.acceptsKeyboard = false
        // Keep the editor tree and frame intact until fully invisible. Shrinking
        // or replacing its content during dismissal causes text/layout snapping.
        widget.fade(visible: false)
        marker.setFrame(clamp(NSRect(x: anchor.x, y: anchor.y - 48, width: 19, height: 48)), display: true)
        marker.fade(visible: !paused)
    }
    func resizeWidget(width: CGFloat, height: CGFloat) {
        guard width.isFinite, height.isFinite, height > 0 else { return }
        widgetSize = NSSize(width: width, height: min(height, available.height - 20))
        positionWidget(animated: expanded && widget.alphaValue > 0.99)
    }
    func positionWidget(animated: Bool) {
        marker.setFrame(clamp(NSRect(x: anchor.x, y: anchor.y - 48, width: 19, height: 48)), display: true)
        var frame = NSRect(origin: NSPoint(x: anchor.x, y: anchor.y - widgetSize.height), size: widgetSize)
        frame = clamp(frame)
        setFrame(widget, frame, animated: animated)
    }
    func clamp(_ rect: NSRect) -> NSRect {
        // Use the window's current monitor if it was dragged away from the widget.
        let bounds = NSScreen.screens.first(where: { $0.visibleFrame.intersects(rect) })?.visibleFrame ?? available
        var rect = rect; rect.size.width = min(rect.width, bounds.width - 12); rect.size.height = min(rect.height, bounds.height - 12)
        rect.origin.x = min(max(rect.minX, bounds.minX + 3), bounds.maxX - rect.width - 3)
        rect.origin.y = min(max(rect.minY, bounds.minY + 6), bounds.maxY - rect.height - 6)
        return rect
    }
    func setFrame(_ panel: NSPanel, _ frame: NSRect, animated: Bool) {
        guard panel.frame != frame else { return }
        positioning = true
        if animated && !NSWorkspace.shared.accessibilityDisplayShouldReduceMotion {
            NSAnimationContext.runAnimationGroup { context in
                context.duration = 0.26; context.timingFunction = CAMediaTimingFunction(name: .easeInEaseOut)
                panel.animator().setFrame(frame, display: true)
            }
        } else { panel.setFrame(frame, display: true) }
        positioning = false
    }
    func openDetail(_ task: Todo?, hover: Bool, today: Bool = true) {
        if let detail, detail.isVisible {
            if detailID == task?.id && task != nil { if !hover { pinDetail(); detail.showForEditing() }; return }
            if detailPinned && hover { return }
            closeDetail()
            guard self.detail == nil else { return }
        }
        var value = task ?? Todo(); if task == nil && today { value.due = Dates.day(Date()) }
        let panel = Self.panel(); detail = panel; detailID = value.id; detailPinned = !hover; detailMoved = false
        panel.delegate = self; configure(panel)
        panel.escape = { [weak self] in self?.closeDetail() }
        let size = NSSize(width: min(420, available.width - 32), height: 330)
        let origin: NSPoint
        if hover {
            let right = widget.frame.maxX + 10
            origin = NSPoint(x: right + size.width <= available.maxX ? right : max(available.minX + 8, widget.frame.minX - size.width - 10), y: widget.frame.maxY - size.height)
        } else { origin = NSPoint(x: available.midX - size.width / 2, y: available.midY - size.height / 2) }
        panel.setFrame(clamp(NSRect(origin: origin, size: size)), display: true)
        panel.contentView = NSHostingView(rootView: DetailView(store: store, controller: self, task: value, isNew: task == nil))
        panel.fade(visible: true)
        if !hover { panel.showForEditing() }
    }
    func pinDetail() { detailPinned = true; detailLeave = nil }
    func resizeDetail(height: CGFloat) {
        guard let detail, height.isFinite, height > 0 else { return }
        let height = min(height, available.height - 12)
        var frame = detail.frame; frame.origin.y += frame.height - height; frame.size.height = height
        setFrame(detail, clamp(frame), animated: true)
    }
    func closeDetail(flush: Bool = true) {
        if flush, let flushDetail, !flushDetail() { pinDetail(); return }
        if let panel = detail { panel.fade(visible: false) { panel.contentView = nil } }
        detail = nil; detailID = nil
        flushDetail = nil; detailPinned = false; detailLeave = nil; leaveTime = Date()
    }
    func windowWillMove(_ notification: Notification) { if !positioning && NSEvent.pressedMouseButtons != 0 { pinDetail(); detailMoved = true } }
    func showArchive() {
        if let archive { archive.fade(visible: true); archive.showForEditing(); return }
        let panel = Self.panel(); configure(panel)
        panel.contentView = NSHostingView(rootView: ArchiveView(store: store, controller: self))
        let size = NSSize(width: 420, height: min(520, available.height - 40))
        panel.setFrame(NSRect(x: available.midX - size.width / 2, y: available.midY - size.height / 2, width: size.width, height: size.height), display: true)
        panel.escape = { [weak self] in self?.hideArchive() }
        archive = panel; panel.fade(visible: true); panel.showForEditing()
    }
    func hideArchive() { archive?.fade(visible: false) }
    func export(markdown: Bool) {
        let panel = NSSavePanel(); panel.nameFieldStringValue = "SideTodo-completed.\(markdown ? "md" : "json")"
        panel.allowedContentTypes = [markdown ? .plainText : .json]
        panel.canCreateDirectories = true
        NSApp.activate(ignoringOtherApps: true)
        guard panel.runModal() == .OK, let url = panel.url else { return }
        do {
            let data = markdown ? Data(store.exportMarkdown().utf8) : try store.exportJSON()
            try data.write(to: url, options: .atomic)
        } catch { store.failure = "내보내지 못했습니다. \(error.localizedDescription)" }
    }
    @objc func importTasks() {
        let panel = NSOpenPanel(); panel.allowedContentTypes = [.json]; panel.allowsMultipleSelection = false
        NSApp.activate(ignoringOtherApps: true)
        guard panel.runModal() == .OK, let url = panel.url else { return }
        do {
            let imported = try JSONDecoder().decode(TaskState.self, from: Data(contentsOf: url))
            let known = Set(store.state.tasks.map(\.id)); var seen = known
            let additions = imported.tasks.filter { seen.insert($0.id).inserted }
            let alert = NSAlert(); alert.messageText = "\(additions.count)개 일정을 가져올까요?"
            alert.informativeText = "기존 일정과 같은 ID는 건너뜁니다. 이 Mac의 기존 일정을 덮어쓰지 않습니다."
            alert.addButton(withTitle: "가져오기"); alert.addButton(withTitle: "취소")
            if alert.runModal() == .alertFirstButtonReturn { if store.commit({ $0.tasks += additions }) { openFromMenu() } }
        } catch { let alert = NSAlert(error: error); alert.runModal() }
    }
}

final class AppDelegate: NSObject, NSApplicationDelegate {
    var controller: DesktopController?
    var smokeDirectory: URL?
    func applicationDidFinishLaunching(_ notification: Notification) {
        NSApp.setActivationPolicy(.accessory)
        // Standard Edit menu is needed for Command-C/V/Z in an accessory app.
        let main = NSMenu(); let app = NSMenuItem(); let appMenu = NSMenu()
        appMenu.addItem(withTitle: "SideTodo 종료", action: #selector(NSApplication.terminate(_:)), keyEquivalent: "q")
        let close = NSMenuItem(title: "창 닫기", action: #selector(closeFocused), keyEquivalent: "w")
        close.target = self; appMenu.addItem(close)
        app.submenu = appMenu; main.addItem(app)
        let edit = NSMenuItem(); edit.title = "편집"; let editMenu = NSMenu(title: "편집")
        for (title, action, key) in [("실행 취소", "undo:", "z"), ("오려두기", "cut:", "x"), ("복사", "copy:", "c"), ("붙여넣기", "paste:", "v"), ("모두 선택", "selectAll:", "a")] {
            editMenu.addItem(withTitle: title, action: NSSelectorFromString(action), keyEquivalent: key)
        }
        edit.submenu = editMenu; main.addItem(edit); NSApp.mainMenu = main
        do {
            let testing = CommandLine.arguments.contains("--ui-smoke")
            let directory = testing ? FileManager.default.temporaryDirectory.appendingPathComponent("SideTodo-smoke-\(UUID())") : FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0].appendingPathComponent("SideTodo")
            if testing { smokeDirectory = directory }
            let store = try TaskStore(url: directory.appendingPathComponent("tasks.json"))
            controller = DesktopController(store: store)
            if testing { runEditorSmoke() }
        } catch {
            let alert = NSAlert(); alert.messageText = "일정 파일을 열지 못했습니다."
            alert.informativeText = "기존 파일 보호를 위해 앱을 종료합니다. ~/Library/Application Support/SideTodo/tasks.json과 .bak 파일을 확인해 주세요.\n\(error.localizedDescription)"
            alert.runModal(); NSApp.terminate(nil)
        }
    }
    func applicationShouldTerminate(_ sender: NSApplication) -> NSApplication.TerminateReply {
        if let flush = controller?.flushDetail, !flush() { return .terminateCancel }
        return .terminateNow
    }
    @objc func closeFocused() {
        guard let controller else { return }
        if controller.detail?.isKeyWindow == true { controller.closeDetail() }
        else if controller.archive?.isKeyWindow == true { controller.hideArchive() }
        else { controller.collapse(force: true) }
    }
    func applicationWillTerminate(_ notification: Notification) {
        controller?.timer?.invalidate()
        if let smokeDirectory { try? FileManager.default.removeItem(at: smokeDirectory) }
    }
    func runUISmoke() {
        guard let controller else { exit(1) }
        var task = Todo(); task.title = "한글 입력 · 길어지는 제목과 줄바꿈 확인"; task.notes = String(repeating: "긴 메모도 줄바꿈하며 읽습니다.\n", count: 20); task.due = Dates.day(Date())
        guard controller.store.put(task) else { exit(1) }
        controller.expand(); controller.openDetail(task, hover: true)
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.25) {
            guard controller.detailPinned == false, controller.detail?.isKeyWindow == false,
                  controller.widget.isKeyWindow == false else {
                fputs("Hover focus check: pinned=\(controller.detailPinned), detailKey=\(controller.detail?.isKeyWindow ?? false), widgetKey=\(controller.widget.isKeyWindow)\n", stderr); exit(1)
            }
            controller.openDetail(task, hover: false)
        }
        DispatchQueue.main.asyncAfter(deadline: .now() + 2) {
            guard controller.expanded, controller.widget.isVisible, let detail = controller.detail, detail.isVisible,
                  detail.frame.height > 250, controller.widget.frame.width >= 250,
                  detail.collectionBehavior.contains(.fullScreenAuxiliary), NSApp.activationPolicy() == .accessory else { exit(1) }
            self.capture(controller.widget, name: "widget")
            self.capture(detail, name: "detail")
            controller.closeDetail(); controller.store.complete(task, done: true); controller.showArchive()
            guard controller.store.archived(newest: true).count == 1, controller.archive?.isVisible == true else { exit(1) }
            controller.store.complete(controller.store.state.tasks[0], done: false)
            guard controller.store.archived(newest: true).isEmpty else { exit(1) }
            controller.collapse(force: true)
            print("PASS: accessory app, floating panels, detail layout, archive, restore, collapse")
            NSApp.terminate(nil)
        }
    }
    func capture(_ panel: NSPanel, name: String) {
        guard let directory = ProcessInfo.processInfo.environment["SIDETODO_SCREENSHOTS"], let view = panel.contentView,
              let rep = view.bitmapImageRepForCachingDisplay(in: view.bounds) else { return }
        view.cacheDisplay(in: view.bounds, to: rep)
        guard let data = rep.representation(using: .png, properties: [:]) else { return }
        let url = URL(fileURLWithPath: directory)
        try? FileManager.default.createDirectory(at: url, withIntermediateDirectories: true)
        try? data.write(to: url.appendingPathComponent("\(name).png"))
    }
}

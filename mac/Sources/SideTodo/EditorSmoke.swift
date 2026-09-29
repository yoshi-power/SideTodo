import AppKit
import Foundation

extension AppDelegate {
    // Drive the actual blank NSTextView through hit testing, window mouse events,
    // first-responder selection and AppKit text input, not just model assignment.
    func runEditorSmoke() {
        Task { @MainActor in
            func pause(_ ms: UInt64) async { try? await Task.sleep(nanoseconds: ms * 1_000_000) }
            func check(_ value: Bool, _ message: String) {
                if !value { fputs("FAIL: \(message)\n", stderr); exit(1) }
                print("PASS: \(message)")
            }
            func editors(_ view: NSView) -> [EntryTextView] {
                (view as? EntryTextView).map { [$0] } ?? view.subviews.flatMap { editors($0) }
            }
            func field(_ panel: NSPanel, _ name: String) -> EntryTextView {
                guard let view = panel.contentView,
                      let field = editors(view).first(where: { $0.identifier?.rawValue == name }) else { fatalError("Missing \(name)") }
                return field
            }
            func click(_ field: EntryTextView, in panel: NSPanel) {
                let location = field.convert(NSPoint(x: field.bounds.midX, y: field.bounds.midY), to: nil)
                let hit = panel.contentView?.hitTest(location)
                check(hit === field || hit?.isDescendant(of: field) == true, "Blank editor center routes clicks to NSTextView")
                let down = NSEvent.mouseEvent(with: .leftMouseDown, location: location, modifierFlags: [], timestamp: 0,
                                             windowNumber: panel.windowNumber, context: nil, eventNumber: 1, clickCount: 1, pressure: 1)!
                let up = NSEvent.mouseEvent(with: .leftMouseUp, location: location, modifierFlags: [], timestamp: 0.1,
                                           windowNumber: panel.windowNumber, context: nil, eventNumber: 2, clickCount: 1, pressure: 0)!
                NSApp.postEvent(up, atStart: true)
                panel.sendEvent(down)
                check(panel.firstResponder === field, "Mouse click makes editor the first responder")
            }
            guard let controller else { exit(1) }
            controller.timer?.invalidate()
            var task = Todo(); task.title = "빈 메모 입력 검사"; task.due = Dates.day(Date())
            check(controller.store.put(task), "Create isolated existing task with empty memo")
            await pause(400)
            check(controller.marker.isVisible && controller.marker.alphaValue > 0.99, "Fresh launch displays the edge marker")
            controller.tick(mouse: NSPoint(x: controller.available.midX, y: controller.available.midY))
            controller.tick(mouse: NSPoint(x: controller.marker.frame.midX, y: controller.marker.frame.midY))
            await pause(400)
            check(controller.expanded && controller.widget.isVisible, "Edge hover opens the widget from cold startup")
            check(controller.widget.contentView.map { !editors($0).isEmpty } ?? false, "Cold-start widget contains real task input, not an empty native content view")
            let firstInput = field(controller.widget, "할 일 추가")
            check(firstInput.bounds.width > 100 && firstInput.bounds.height >= 20, "Task input is laid out and drawable on first hover")
            self.capture(controller.widget, name: "cold-start-widget")
            controller.openDetail(task, hover: true)
            await pause(400)
            check(!controller.detailPinned && controller.detail?.isKeyWindow == false && !controller.widget.isKeyWindow, "Hover does not take keyboard focus")
            guard let panel = controller.detail else { exit(1) }
            let memo = field(panel, "일정 메모")
            check(memo.bounds.height >= 96 && memo.bounds.width > 200, "Empty memo keeps its full 100pt click region")
            click(memo, in: panel)
            memo.insertText("한글 메모 입력\n두 번째 줄", replacementRange: NSRange(location: NSNotFound, length: 0))
            await pause(750)
            check(controller.store.state.tasks[0].notes == "한글 메모 입력\n두 번째 줄", "Native memo input reaches autosave")
            check(controller.detailPinned, "Clicking and typing pins hover editor")
            let title = field(panel, "일정 제목")
            panel.makeFirstResponder(title)
            title.insertText("수정한 제목", replacementRange: NSRange(location: 0, length: (title.string as NSString).length))
            await pause(600)
            check(controller.store.state.tasks[0].title == "수정한 제목" && controller.store.state.tasks[0].notes.contains("두 번째 줄"), "Title and memo remain independently editable")
            let smallHeight = panel.frame.height
            panel.makeFirstResponder(memo)
            memo.insertText(String(repeating: "길게 작성하는 메모도 보입니다.\n", count: 24), replacementRange: NSRange(location: 0, length: (memo.string as NSString).length))
            await pause(800)
            check(memo.bounds.height > 300 && panel.frame.height > smallHeight, "Long memo expands editor and panel")
            self.capture(panel, name: "detail")
            self.capture(controller.widget, name: "widget")
            controller.closeDetail()
            await pause(350)
            let reloaded = try! TaskStore(url: controller.store.url)
            check(reloaded.state.tasks[0].notes == memo.string, "Memo survives closing and reloading from disk")
            // Reopen the same item via the explicit path and insert again.
            controller.openDetail(reloaded.state.tasks[0], hover: false)
            await pause(400)
            let explicit = controller.detail!, again = field(explicit, "일정 메모")
            explicit.makeFirstResponder(again)
            again.insertText("끝 메모", replacementRange: NSRange(location: (again.string as NSString).length, length: 0))
            await pause(700); controller.closeDetail(); await pause(350)
            check(controller.store.state.tasks[0].notes.hasSuffix("끝 메모"), "Explicit editor also saves memo changes")
            controller.store.complete(controller.store.state.tasks[0], done: true)
            controller.showArchive(); await pause(350)
            check(controller.store.archived(newest: true).count == 1, "Completion archive retains edited task")
            controller.store.complete(controller.store.state.tasks[0], done: false); controller.hideArchive()
            controller.expand(); await pause(350)
            let quick = field(controller.widget, "할 일 추가")
            click(quick, in: controller.widget)
            quick.insertText("위젯에서 추가한 일정", replacementRange: NSRange(location: 0, length: (quick.string as NSString).length))
            await pause(150)
            let enter = NSEvent.keyEvent(with: .keyDown, location: .zero, modifierFlags: [], timestamp: 0,
                                        windowNumber: controller.widget.windowNumber, context: nil, characters: "\r",
                                        charactersIgnoringModifiers: "\r", isARepeat: false, keyCode: 36)!
            controller.widget.sendEvent(enter); await pause(500)
            check(controller.store.state.tasks.contains { $0.title == "위젯에서 추가한 일정" }, "Visible widget accepts input and Return saves an actual task")
            let bounds = controller.widget.frame
            controller.collapse(force: true); await pause(90)
            if !NSWorkspace.shared.accessibilityDisplayShouldReduceMotion {
                check(controller.widget.isVisible && controller.widget.alphaValue > 0 && controller.widget.alphaValue < 1 && controller.widget.frame == bounds, "Dismissal keeps layout intact while fading")
            }
            controller.expand(); await pause(400)
            check(controller.expanded && controller.widget.isVisible && controller.widget.alphaValue > 0.99, "Interrupted dismissal reverses without stale hiding")
            controller.collapse(force: true); await pause(400)
            check(!controller.widget.isVisible && controller.marker.isVisible && controller.marker.frame.width == 19, "Idle marker is visible and hidden panel no longer intercepts clicks")
            self.capture(controller.marker, name: "marker")
            controller.openFromMenu(); await pause(400)
            check(!editors(controller.widget.contentView!).isEmpty && controller.widget.alphaValue > 0.99, "Menu reopens actual widget contents after full dismissal")
            NSApp.terminate(nil)
        }
    }
}

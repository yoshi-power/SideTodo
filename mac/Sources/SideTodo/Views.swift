import AppKit
import SwiftUI
import UniformTypeIdentifiers

enum Style {
    static let ink = Color.white.opacity(0.93)
    static let muted = Color.white.opacity(0.48)
    static let peach = Color(red: 0.91, green: 0.79, blue: 0.71)
    static var motion: Animation? {
        NSWorkspace.shared.accessibilityDisplayShouldReduceMotion ? nil : .easeInOut(duration: 0.22)
    }
}

struct Surface<Content: View>: View {
    @ViewBuilder var content: Content
    var body: some View {
        content.padding(16)
            .foregroundStyle(Style.ink)
            .background(.ultraThinMaterial)
            .background(Color.black.opacity(0.68))
            .clipShape(RoundedRectangle(cornerRadius: 18, style: .continuous))
            .overlay(RoundedRectangle(cornerRadius: 18).stroke(.white.opacity(0.12), lineWidth: 1))
            .environment(\.colorScheme, .dark)
    }
}

struct Glyph: View {
    var symbol: String
    var label: String
    var action: () -> Void
    var body: some View {
        Button(action: action) { Image(systemName: symbol).font(.system(size: 13, weight: .medium)).frame(width: 26, height: 26) }
            .buttonStyle(.plain).foregroundStyle(Style.muted).help(label).accessibilityLabel(label)
    }
}

struct HeightKey: PreferenceKey {
    static var defaultValue: CGFloat = 0
    static func reduce(value: inout CGFloat, nextValue: () -> CGFloat) { value = max(value, nextValue()) }
}

extension View {
    func measureHeight(_ action: @escaping (CGFloat) -> Void) -> some View {
        background(GeometryReader { geo in Color.clear.preference(key: HeightKey.self, value: geo.size.height) })
            .onPreferenceChange(HeightKey.self, perform: action)
    }
}

// NSTextView keeps macOS selection, undo, spell checking and Korean IME behavior.
// Return submits only after the input method finishes composing; Shift-Return adds a line.
final class EntryTextView: NSTextView {
    var submit: (() -> Void)?
    var escape: (() -> Void)?
    var tabForward: (() -> Void)?
    override func keyDown(with event: NSEvent) {
        if event.keyCode == 36 && !event.modifierFlags.contains(.shift) && !hasMarkedText(), let submit {
            submit(); return
        }
        if event.keyCode == 53 && !hasMarkedText() { escape?(); return }
        if event.keyCode == 48 && !hasMarkedText() { window?.selectNextKeyView(self); return }
        super.keyDown(with: event)
    }
    override func becomeFirstResponder() -> Bool {
        let result = super.becomeFirstResponder()
        if result { (delegate as? Editor.Coordinator)?.focused(true) }
        return result
    }
    override func resignFirstResponder() -> Bool {
        let result = super.resignFirstResponder()
        if result { (delegate as? Editor.Coordinator)?.focused(false) }
        return result
    }
}

struct Editor: NSViewRepresentable {
    @Binding var text: String
    @Binding var height: CGFloat
    var minHeight: CGFloat = 24
    var fontSize: CGFloat = 14
    var submit: (() -> Void)? = nil
    var escape: (() -> Void)? = nil
    var focused: (Bool) -> Void = { _ in }
    var accessibility: String
    func makeCoordinator() -> Coordinator { Coordinator(self) }
    func makeNSView(context: Context) -> EntryTextView {
        let view = EntryTextView()
        view.delegate = context.coordinator; view.isRichText = false
        view.drawsBackground = false; view.textColor = .white
        view.insertionPointColor = .white; view.font = .systemFont(ofSize: fontSize)
        view.isVerticallyResizable = true; view.isHorizontallyResizable = false
        view.textContainerInset = NSSize(width: 0, height: 3)
        view.textContainer?.lineFragmentPadding = 0
        view.textContainer?.widthTracksTextView = true
        view.allowsUndo = true; view.isAutomaticQuoteSubstitutionEnabled = false
        view.setAccessibilityLabel(accessibility)
        return view
    }
    func updateNSView(_ view: EntryTextView, context: Context) {
        context.coordinator.parent = self
        if view.string != text && !view.hasMarkedText() { view.string = text }
        view.submit = submit; view.escape = escape
        DispatchQueue.main.async { context.coordinator.measure(view) }
    }
    final class Coordinator: NSObject, NSTextViewDelegate {
        var parent: Editor
        init(_ parent: Editor) { self.parent = parent }
        func focused(_ value: Bool) { DispatchQueue.main.async { self.parent.focused(value) } }
        func textDidChange(_ notification: Notification) {
            guard let view = notification.object as? NSTextView else { return }
            parent.text = view.string; measure(view)
        }
        func measure(_ view: NSTextView) {
            guard let container = view.textContainer, let manager = view.layoutManager else { return }
            manager.ensureLayout(for: container)
            let measured = max(parent.minHeight, ceil(manager.usedRect(for: container).height) + 8)
            if abs(parent.height - measured) > 1 { parent.height = measured }
        }
    }
}

struct WidgetView: View {
    @ObservedObject var store: TaskStore
    let controller: DesktopController
    @State private var today = true
    @State private var draft = ""
    @State private var entryHeight: CGFloat = 24
    @State private var focused = false
    @State private var writing = false
    @State private var shrink: DispatchWorkItem?
    @State private var hover: DispatchWorkItem?
    @State private var listHeight: CGFloat = 30
    var items: [Todo] { store.state.tasks.filter { !$0.done && $0.isToday == today } }
    var width: CGFloat {
        let longest = items.map { ($0.title as NSString).size(withAttributes: [.font: NSFont.systemFont(ofSize: 13)]).width }.max() ?? 0
        return min(controller.available.width - 24, max(writing ? 340 : 252, min(420, longest + 88)))
    }
    var body: some View {
        Surface {
            VStack(alignment: .leading, spacing: 10) {
                HStack(spacing: 14) {
                    tab("오늘", selected: today) { today = true }
                    tab("앞으로", selected: !today) { today = false }
                    Spacer(minLength: 4)
                    Glyph(symbol: "archivebox", label: "완료 기록") { controller.showArchive() }
                    Glyph(symbol: "plus", label: "자세한 일정 추가") { controller.openDetail(nil, hover: false, today: today) }
                }
                ScrollView {
                    VStack(alignment: .leading, spacing: 4) {
                        ForEach(items) { task in
                            HStack(alignment: .top, spacing: 8) {
                                Button { withAnimation(Style.motion) { store.complete(task, done: true) } } label: {
                                    Image(systemName: "square").font(.system(size: 15)).foregroundStyle(Style.muted)
                                }.buttonStyle(.plain).padding(.top, 2).accessibilityLabel("\(task.title) 완료")
                                Text(task.title).font(.system(size: 13)).fixedSize(horizontal: false, vertical: true).frame(maxWidth: .infinity, alignment: .leading)
                                if !task.notes.isEmpty { Image(systemName: "text.alignleft").font(.system(size: 9)).foregroundStyle(Style.muted).padding(.top, 5) }
                                Glyph(symbol: "ellipsis", label: "일정 편집") { controller.openDetail(task, hover: false) }
                            }.padding(.vertical, 5).contentShape(Rectangle())
                                .onHover { inside in
                                    hover?.cancel()
                                    if inside {
                                        let work = DispatchWorkItem { controller.openDetail(task, hover: true) }
                                        hover = work; DispatchQueue.main.asyncAfter(deadline: .now() + 0.65, execute: work)
                                    }
                                }
                        }
                        if items.isEmpty { Text("비어 있어요").font(.system(size: 12)).foregroundStyle(Style.muted).padding(.vertical, 8) }
                    }.frame(maxWidth: .infinity, alignment: .leading).measureHeight { listHeight = $0 }
                }.frame(height: min(listHeight, controller.available.height * 0.48))
                HStack(alignment: .top, spacing: 8) {
                    Image(systemName: "square").font(.system(size: 15)).foregroundStyle(Style.muted).padding(.top, 5)
                    ZStack(alignment: .topLeading) {
                        if draft.isEmpty { Text("할 일 추가").font(.system(size: 13)).foregroundStyle(Style.muted).padding(.top, 4).allowsHitTesting(false) }
                        Editor(text: $draft, height: $entryHeight, submit: add, escape: { controller.collapse(force: true) }, focused: { value in
                            focused = value; controller.typing = value
                            if value { expandForWriting() } else { settle() }
                        }, accessibility: "할 일 추가").frame(height: entryHeight)
                    }
                }.padding(writing ? 10 : 3)
                    .background(RoundedRectangle(cornerRadius: 10).fill(.white.opacity(writing ? 0.07 : 0)))
                if writing { Text("↵ 추가   ⇧↵ 줄바꿈").font(.system(size: 10)).foregroundStyle(Style.muted).transition(.opacity) }
                if let failure = store.failure { Text(failure).font(.caption).foregroundStyle(Style.peach).textSelection(.enabled) }
            }
        }.frame(width: width).fixedSize(horizontal: false, vertical: true)
            .measureHeight { controller.resizeWidget(width: width, height: $0) }
            .onChange(of: width) { _ in controller.resizeWidget(width: width, height: controller.widget.frame.height) }
            .onChange(of: draft) { _ in expandForWriting() }
            .onChange(of: draft) { controller.quickDraft = $0 }
            .onChange(of: today) { controller.selectedToday = $0 }
            .onAppear { draft = controller.quickDraft; today = controller.selectedToday }
            .onDisappear { hover?.cancel(); shrink?.cancel(); controller.typing = false }
    }
    func tab(_ title: String, selected: Bool, action: @escaping () -> Void) -> some View {
        Button(action: action) { Text(title).font(.system(size: 12, weight: selected ? .semibold : .regular)).foregroundStyle(selected ? Style.ink : Style.muted) }
            .buttonStyle(.plain).accessibilityAddTraits(selected ? .isSelected : [])
    }
    func expandForWriting() { shrink?.cancel(); withAnimation(Style.motion) { writing = true } }
    func settle() {
        shrink?.cancel()
        let work = DispatchWorkItem { withAnimation(Style.motion) { writing = false } }
        shrink = work; DispatchQueue.main.asyncAfter(deadline: .now() + 1.3, execute: work)
    }
    func add() {
        let title = draft.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !title.isEmpty else { return }
        var task = Todo(); task.title = title; task.due = today ? Dates.day(Date()) : nil
        if store.put(task) { draft = ""; settle() }
    }
}

struct DetailView: View {
    @ObservedObject var store: TaskStore
    let controller: DesktopController
    @State var task: Todo
    let isNew: Bool
    @State private var titleHeight: CGFloat = 32
    @State private var notesHeight: CGFloat = 100
    @State private var calendar = false
    @State private var month = Date()
    @State private var contentHeight: CGFloat = 200
    @State private var saved: DispatchWorkItem?
    var body: some View {
        Surface {
            VStack(spacing: 12) {
                HStack {
                    DragHandle().frame(height: 26).overlay(alignment: .leading) {
                        Text(isNew ? "새 일정" : "일정").font(.system(size: 11, weight: .medium)).foregroundStyle(Style.muted).allowsHitTesting(false)
                    }
                    Glyph(symbol: "xmark", label: "닫기") { close() }
                }
                ScrollView {
                    VStack(alignment: .leading, spacing: 14) {
                        ZStack(alignment: .topLeading) {
                            if task.title.isEmpty { Text("어떤 일을 할까요?").font(.system(size: 20, weight: .medium)).foregroundStyle(Style.muted).padding(.top, 4).allowsHitTesting(false) }
                            Editor(text: $task.title, height: $titleHeight, minHeight: 32, fontSize: 20, focused: touch, accessibility: "일정 제목").frame(height: titleHeight)
                        }
                        HStack(spacing: 6) {
                            chip("오늘", icon: "sun.max", selected: task.due.map { Calendar.current.isDateInToday(Dates.local($0) ?? .distantPast) } ?? false) { task.due = Dates.day(Date()); calendar = false }
                            chip("앞으로", icon: "tray", selected: task.due == nil) { task.due = nil; calendar = false }
                            chip(task.due == nil ? "날짜" : task.dueLabel, icon: "calendar", selected: calendar) {
                                touch(true); month = Dates.local(task.due) ?? Date(); withAnimation(Style.motion) { calendar.toggle() }
                            }
                        }
                        if calendar { CalendarGrid(month: $month, selected: Dates.local(task.due)) { task.due = Dates.day($0); withAnimation(Style.motion) { calendar = false } } }
                        Rectangle().fill(.white.opacity(0.08)).frame(height: 1)
                        ZStack(alignment: .topLeading) {
                            if task.notes.isEmpty { Text("메모").font(.system(size: 14)).foregroundStyle(Style.muted).padding(.top, 4).allowsHitTesting(false) }
                            Editor(text: $task.notes, height: $notesHeight, minHeight: 100, focused: touch, accessibility: "일정 메모").frame(height: notesHeight)
                        }
                        if let failure = store.failure { Text(failure).font(.caption).foregroundStyle(Style.peach).textSelection(.enabled) }
                    }.padding(.horizontal, 2).frame(maxWidth: .infinity, alignment: .leading).measureHeight { contentHeight = $0 }
                }.frame(height: min(contentHeight, max(180, controller.available.height - 160)))
                HStack {
                    if !isNew { Text("자동 저장").font(.system(size: 10)).foregroundStyle(Style.muted) }
                    Spacer()
                    Button(isNew ? "추가" : "완료") { close(saveNew: true) }
                        .buttonStyle(.plain).font(.system(size: 12, weight: .medium)).padding(.horizontal, 14).padding(.vertical, 7)
                        .background(Style.peach.opacity(0.16), in: Capsule()).foregroundStyle(Style.peach)
                        .disabled(task.title.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty)
                        .keyboardShortcut(.return, modifiers: .command)
                }
            }
        }.frame(width: min(420, controller.available.width - 32)).fixedSize(horizontal: false, vertical: true)
            .measureHeight { controller.resizeDetail(height: $0) }
            .onChange(of: task) { _ in
                controller.pinDetail(); saved?.cancel()
                if !isNew { let work = DispatchWorkItem { _ = persist() }; saved = work; DispatchQueue.main.asyncAfter(deadline: .now() + 0.45, execute: work) }
            }
            .onAppear { controller.flushDetail = flush }
            .onDisappear { saved?.cancel() }
    }
    func touch(_ active: Bool) { if active { controller.pinDetail() } }
    func flush() -> Bool {
        saved?.cancel()
        if !isNew { return persist() }
        if task.title.isEmpty && task.notes.isEmpty { return true }
        let alert = NSAlert(); alert.messageText = "작성 중인 일정을 저장할까요?"
        alert.addButton(withTitle: "저장"); alert.addButton(withTitle: "취소"); alert.addButton(withTitle: "버리기")
        switch alert.runModal() {
        case .alertFirstButtonReturn: return persist()
        case .alertThirdButtonReturn: return true
        default: return false
        }
    }
    func chip(_ text: String, icon: String, selected: Bool, action: @escaping () -> Void) -> some View {
        Button { touch(true); action() } label: {
            Label(text, systemImage: icon).font(.system(size: 11, weight: .medium)).padding(.horizontal, 10).padding(.vertical, 7)
                .foregroundStyle(selected ? Style.peach : Style.muted)
                .background(selected ? Style.peach.opacity(0.13) : Color.white.opacity(0.04), in: Capsule())
        }.buttonStyle(.plain)
    }
    func persist() -> Bool {
        var value = task; value.title = value.title.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !value.title.isEmpty else { store.failure = "제목을 입력해 주세요."; return false }
        return store.put(value)
    }
    func close(saveNew: Bool = false) {
        saved?.cancel()
        if isNew {
            if saveNew { guard persist() else { return } }
            else if !task.title.isEmpty || !task.notes.isEmpty {
                let alert = NSAlert(); alert.messageText = "작성 중인 일정을 버릴까요?"
                alert.addButton(withTitle: "계속 작성"); alert.addButton(withTitle: "버리기")
                guard alert.runModal() == .alertSecondButtonReturn else { return }
            }
        } else { guard persist() else { return } }
        controller.closeDetail(flush: false)
    }
}

struct DragHandle: NSViewRepresentable {
    final class Handle: NSView {
        override func mouseDown(with event: NSEvent) { window?.performDrag(with: event) }
        override func resetCursorRects() { addCursorRect(bounds, cursor: .openHand) }
    }
    func makeNSView(context: Context) -> Handle { Handle() }
    func updateNSView(_ view: Handle, context: Context) {}
}

struct CalendarGrid: View {
    @Binding var month: Date
    var selected: Date?
    var pick: (Date) -> Void
    private var cal: Calendar { Calendar.current }
    private var start: Date { cal.date(from: cal.dateComponents([.year, .month], from: month))! }
    private var offset: Int { (cal.component(.weekday, from: start) - cal.firstWeekday + 7) % 7 }
    var body: some View {
        VStack(spacing: 12) {
            HStack {
                Glyph(symbol: "chevron.left", label: "이전 달") { shift(-1) }
                Spacer(); Text(month.formatted(.dateTime.year().month(.wide))).font(.system(size: 12, weight: .semibold)); Spacer()
                Glyph(symbol: "chevron.right", label: "다음 달") { shift(1) }
            }
            LazyVGrid(columns: Array(repeating: GridItem(.flexible(), spacing: 4), count: 7), spacing: 5) {
                ForEach(0..<7, id: \.self) { index in
                    Text(cal.veryShortStandaloneWeekdaySymbols[(index + cal.firstWeekday - 1) % 7]).font(.system(size: 10)).foregroundStyle(Style.muted)
                }
                ForEach(0..<offset + (cal.range(of: .day, in: .month, for: month)?.count ?? 30), id: \.self) { index in
                    if index < offset { Color.clear.frame(height: 28) }
                    else {
                        let date = cal.date(byAdding: .day, value: index - offset, to: start)!
                        let chosen = selected.map { cal.isDate($0, inSameDayAs: date) } ?? false
                        Button { pick(date) } label: {
                            Text("\(index - offset + 1)").font(.system(size: 12, weight: cal.isDateInToday(date) ? .bold : .regular))
                                .frame(maxWidth: .infinity).frame(height: 28)
                                .foregroundStyle(chosen ? Color.black : cal.isDateInToday(date) ? Style.peach : Style.ink)
                                .background(chosen ? Style.peach : .clear, in: RoundedRectangle(cornerRadius: 8))
                        }.buttonStyle(.plain).accessibilityLabel(date.formatted(date: .complete, time: .omitted))
                    }
                }
            }
        }.padding(12).background(.white.opacity(0.04), in: RoundedRectangle(cornerRadius: 12))
    }
    func shift(_ value: Int) { month = cal.date(byAdding: .month, value: value, to: start)! }
}

struct ArchiveView: View {
    @ObservedObject var store: TaskStore
    let controller: DesktopController
    @State private var newest = true
    var body: some View {
        Surface {
            VStack(alignment: .leading, spacing: 14) {
                HStack {
                    Text("완료 기록").font(.system(size: 14, weight: .semibold))
                    DragHandle().frame(height: 26)
                    Glyph(symbol: "xmark", label: "닫기") { controller.archive?.orderOut(nil) }
                }
                HStack {
                    Button(newest ? "최근 완료순 ↓" : "완료한 순서 ↑") { newest.toggle() }.buttonStyle(.plain).font(.system(size: 11)).foregroundStyle(Style.muted)
                    Spacer()
                    Menu { Button("JSON") { controller.export(markdown: false) }; Button("Markdown") { controller.export(markdown: true) } } label: {
                        Image(systemName: "square.and.arrow.up")
                    }.menuStyle(.borderlessButton).fixedSize().help("완료 기록 내보내기")
                }
                ScrollView {
                    LazyVStack(alignment: .leading, spacing: 14) {
                        ForEach(store.archived(newest: newest)) { task in
                            HStack(alignment: .top, spacing: 12) {
                                VStack(alignment: .leading, spacing: 5) {
                                    Text(task.title).font(.system(size: 13)).fixedSize(horizontal: false, vertical: true)
                                    Text(Dates.instant(task.completedAt)?.formatted(date: .abbreviated, time: .shortened) ?? "시각 미기록").font(.system(size: 10)).foregroundStyle(Style.muted)
                                    if !task.notes.isEmpty { Text(task.notes).font(.system(size: 12)).foregroundStyle(Style.muted).fixedSize(horizontal: false, vertical: true).textSelection(.enabled) }
                                }.frame(maxWidth: .infinity, alignment: .leading)
                                Glyph(symbol: "arrow.uturn.backward", label: "\(task.title) 복구") { store.complete(task, done: false) }
                            }
                        }
                        if store.archived(newest: newest).isEmpty { Text("완료한 일정이 여기에 모입니다.").font(.system(size: 12)).foregroundStyle(Style.muted) }
                    }.padding(.vertical, 5)
                }
                if let error = store.failure { Text(error).font(.caption).foregroundStyle(Style.peach) }
            }
        }.frame(width: 420, height: min(520, controller.available.height - 40))
    }
}

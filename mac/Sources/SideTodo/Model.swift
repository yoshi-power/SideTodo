import Foundation
import Combine

enum Dates {
    static func stamp(_ date: Date = Date()) -> String {
        ISO8601DateFormatter().string(from: date)
    }
    static func day(_ date: Date) -> String {
        let f = DateFormatter(); f.locale = Locale(identifier: "en_US_POSIX")
        f.calendar = Calendar(identifier: .gregorian); f.dateFormat = "yyyy-MM-dd"
        return f.string(from: date)
    }
    static func local(_ value: String?) -> Date? {
        guard let value else { return nil }
        let f = DateFormatter(); f.locale = Locale(identifier: "en_US_POSIX")
        f.calendar = Calendar(identifier: .gregorian); f.dateFormat = "yyyy-MM-dd"
        f.isLenient = false
        return f.date(from: String(value.prefix(10)))
    }
    static func instant(_ value: String?) -> Date? {
        guard let value else { return nil }
        let f = ISO8601DateFormatter()
        if let d = f.date(from: value) { return d }
        f.formatOptions.insert(.withFractionalSeconds)
        return f.date(from: value)
    }
}

struct Todo: Codable, Identifiable, Equatable {
    var id = UUID()
    var title = ""
    var notes = ""
    var due: String?
    var done = false
    var completedAt: String?
    var created = Dates.stamp()
    enum CodingKeys: String, CodingKey {
        case id = "Id", title = "Title", notes = "Notes", due = "Due"
        case done = "Done", completedAt = "CompletedAt", created = "Created"
    }
    var isToday: Bool {
        guard let date = Dates.local(due) else { return false }
        return date <= Calendar.current.startOfDay(for: Date())
    }
    var dueLabel: String {
        guard let date = Dates.local(due) else { return "앞으로" }
        if Calendar.current.isDateInToday(date) { return "오늘" }
        return date.formatted(.dateTime.month(.abbreviated).day())
    }
}

struct TaskState: Codable {
    var tasks: [Todo] = []
    var y: Double = 160
    enum CodingKeys: String, CodingKey { case tasks = "Tasks", y = "Y" }
}

final class TaskStore: ObservableObject {
    @Published private(set) var state: TaskState
    @Published var failure: String?
    let url: URL
    init(url: URL) throws {
        self.url = url
        state = FileManager.default.fileExists(atPath: url.path)
            ? try JSONDecoder().decode(TaskState.self, from: Data(contentsOf: url)) : TaskState()
        guard Set(state.tasks.map(\.id)).count == state.tasks.count else {
            throw CocoaError(.fileReadCorruptFile)
        }
    }
    @discardableResult func commit(_ mutation: (inout TaskState) -> Void) -> Bool {
        var next = state; mutation(&next)
        do {
            let encoder = JSONEncoder(); encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
            let data = try encoder.encode(next)
            try FileManager.default.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
            // Read and back up the previous good file before replacing the primary atomically.
            if FileManager.default.fileExists(atPath: url.path) {
                let previous = try Data(contentsOf: url)
                try previous.write(to: url.appendingPathExtension("bak"), options: .atomic)
            }
            try data.write(to: url, options: .atomic)
            state = next; failure = nil; return true
        } catch {
            failure = "저장하지 못했습니다. \(error.localizedDescription)"
            return false
        }
    }
    @discardableResult func put(_ task: Todo) -> Bool {
        commit { state in
            if let index = state.tasks.firstIndex(where: { $0.id == task.id }) { state.tasks[index] = task }
            else { state.tasks.append(task) }
        }
    }
    func complete(_ task: Todo, done: Bool) {
        var task = task; task.done = done; task.completedAt = done ? Dates.stamp() : nil
        put(task)
    }
    func archived(newest: Bool) -> [Todo] {
        state.tasks.filter(\.done).sorted {
            let a = Dates.instant($0.completedAt), b = Dates.instant($1.completedAt)
            if a == b { return $0.created < $1.created }
            guard let a else { return false }; guard let b else { return true }
            return newest ? a > b : a < b
        }
    }
    func exportJSON() throws -> Data {
        let encoder = JSONEncoder(); encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
        return try encoder.encode(TaskState(tasks: archived(newest: false)))
    }
    func exportMarkdown() -> String {
        "# SideTodo · 완료 기록\n\n" + archived(newest: false).map { task in
            "- [x] \(task.title.replacingOccurrences(of: "\n", with: " "))\n" +
            "  - 완료: \(task.completedAt ?? "시각 미기록")\n" +
            "  - 일정: \(task.due ?? "날짜 없음")\n" +
            task.notes.split(separator: "\n", omittingEmptySubsequences: false).map { "  > \($0)" }.joined(separator: "\n")
        }.joined(separator: "\n\n") + "\n"
    }
}

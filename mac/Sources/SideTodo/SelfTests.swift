import Foundation

enum SelfTests {
    struct Failure: Error { var message: String }
    static func check(_ condition: @autoclosure () -> Bool, _ message: String) throws {
        if !condition() { throw Failure(message: message) }
    }
    static func run() throws {
        let directory = FileManager.default.temporaryDirectory.appendingPathComponent("SideTodo-tests-\(UUID())")
        defer { try? FileManager.default.removeItem(at: directory) }
        let url = directory.appendingPathComponent("tasks.json")
        let store = try TaskStore(url: url)
        var task = Todo(); task.title = "한글 일정"; task.notes = "첫째 줄\n둘째 줄 🐈"; task.due = Dates.day(Date())
        try check(task.isToday, "Today grouping")
        try check(store.put(task), "Initial save")
        store.complete(task, done: true)
        let reload = try TaskStore(url: url)
        try check(reload.state.tasks[0].notes == task.notes, "Unicode round trip")
        try check(reload.state.tasks[0].completedAt != nil, "Completion timestamp")
        try check(FileManager.default.fileExists(atPath: url.appendingPathExtension("bak").path), "Backup")
        reload.complete(reload.state.tasks[0], done: false)
        try check(reload.state.tasks[0].completedAt == nil && !reload.state.tasks[0].done, "Restore")
        try check(reload.state.tasks[0].due == task.due && reload.state.tasks[0].notes == task.notes, "Restore retains details")
        var older = Todo(); older.title = "먼저 완료"; older.done = true; older.completedAt = "2025-01-01T12:00:00+09:00"
        var newer = Todo(); newer.title = "나중 완료"; newer.done = true; newer.completedAt = "2025-01-01T05:00:00Z"
        var legacy = Todo(); legacy.title = "시각 미기록"; legacy.done = true
        try check(reload.put(older) && reload.put(newer) && reload.put(legacy), "Archive fixtures")
        try check(reload.archived(newest: true).first?.id == newer.id, "Offset-aware ordering")
        try check(reload.archived(newest: false).first?.id == older.id && reload.archived(newest: false).last?.id == legacy.id, "Oldest and unknown ordering")
        let export = try JSONDecoder().decode(TaskState.self, from: reload.exportJSON())
        try check(export.tasks.count == 3 && export.tasks.allSatisfy(\.done), "JSON archive excludes active items")
        try check(reload.exportMarkdown().contains("시각 미기록"), "Markdown archive")
        let fixture = #"{"Tasks":[{"Id":"956c6d24-7908-4e78-bb30-bd0590d81a12","Title":"Windows 가져오기","Notes":"메모","Due":"2026-10-03T00:00:00","Done":true,"CompletedAt":"2026-09-29T16:14:11.1234567+09:00","Created":"2026-09-28T19:04:00.1234567+09:00"}],"Y":160}"#
        let imported = try JSONDecoder().decode(TaskState.self, from: Data(fixture.utf8))
        try check(imported.tasks[0].notes == "메모" && Dates.local(imported.tasks[0].due) != nil, "Windows schema")
        try check(Dates.instant(imported.tasks[0].completedAt) != nil, "Windows subsecond timestamp")
        // Persistence failure must not mark an item complete in memory.
        let failedURL = directory.appendingPathComponent("blocked/tasks.json")
        let blocked = try TaskStore(url: failedURL)
        try Data("not a directory".utf8).write(to: directory.appendingPathComponent("blocked"))
        try check(!blocked.put(task) && blocked.state.tasks.isEmpty && blocked.failure != nil, "Transactional save failure")
        try Data("broken".utf8).write(to: url)
        do { _ = try TaskStore(url: url); throw Failure(message: "Corruption accepted") }
        catch is DecodingError { }
        let preserved = try String(contentsOf: url, encoding: .utf8)
        try check(preserved == "broken", "Corrupt data preserved")
        print("PASS: persistence, backup, Korean text, completion, restore, archive order, JSON/Markdown, Windows schema, failed writes, corruption")
    }
}

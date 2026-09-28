using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Encodings.Web;

namespace SideTodo;

public static class CompletionArchive
{
    public static void SetDone(Todo task, bool done)
    {
        if (task.Done == done) return;
        task.Done = done; task.CompletedAt = done ? DateTimeOffset.Now : null;
    }
    // Older versions did not record completion time. Preserve unknown as null.
    public static List<Todo> Items(IEnumerable<Todo> tasks, bool newestFirst = true) =>
        tasks.Where(t => t.Done).OrderBy(t => !t.CompletedAt.HasValue)
            .ThenBy(t => newestFirst ? -(t.CompletedAt?.UtcTicks ?? 0) : t.CompletedAt?.UtcTicks ?? 0)
            .ThenBy(t => t.Created).ThenBy(t => t.Id).ToList();
    public static string Json(IEnumerable<Todo> tasks) => JsonSerializer.Serialize(new
    {
        schemaVersion = 1, exportedAt = DateTimeOffset.Now, order = "completion_time_ascending_unknown_last",
        tasks = Items(tasks, false).Select(t => new { id = t.Id, title = t.Title, notes = t.Notes, scheduledDate = t.Due?.ToString("yyyy-MM-dd"), createdAt = t.Created, completedAt = t.CompletedAt, status = "completed" })
    }, new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    public static string Markdown(IEnumerable<Todo> tasks)
    {
        var output = new StringBuilder("# SideTodo 완료 기록\n\n");
        foreach (var task in Items(tasks, false))
        {
            output.AppendLine("## " + task.Title.Replace("\r", " ").Replace("\n", " "));
            output.AppendLine("- ID: " + task.Id);
            output.AppendLine("- 완료: " + (task.CompletedAt?.ToString("yyyy-MM-dd HH:mm:ss zzz") ?? "시간 미기록"));
            output.AppendLine("- 생성: " + task.Created.ToString("yyyy-MM-dd HH:mm:ss"));
            output.AppendLine("- 예정일: " + (task.Due?.ToString("yyyy-MM-dd") ?? "날짜 없음"));
            if (!string.IsNullOrWhiteSpace(task.Notes)) { output.AppendLine(); output.AppendLine(task.Notes); }
            output.AppendLine();
        }
        return output.ToString();
    }
}

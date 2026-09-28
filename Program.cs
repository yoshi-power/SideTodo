using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace SideTodo;

public sealed class Todo
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "";
    public string Notes { get; set; } = "";
    public DateTime? Due { get; set; }
    public bool Done { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTime Created { get; set; } = DateTime.Now;
}
public sealed class State
{
    public List<Todo> Tasks { get; set; } = new();
    public double Y { get; set; } = 160;
}
public sealed class Store
{
    public string PathName { get; }
    public State Data { get; private set; } = new();
    public Store(string path) { PathName = path; }
    public void Load()
    {
        if (!File.Exists(PathName)) return;
        Data = JsonSerializer.Deserialize<State>(File.ReadAllText(PathName)) ?? throw new InvalidDataException("저장 파일이 비어 있습니다.");
        if (Data.Tasks == null || Data.Tasks.Any(t => t == null || t.Title == null)) throw new InvalidDataException("저장 형식이 올바르지 않습니다.");
    }
    public void Save()
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PathName)!);
        var temporary = PathName + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(Data, new JsonSerializerOptions { WriteIndented = true }));
        if (File.Exists(PathName)) File.Replace(temporary, PathName, PathName + ".bak");
        else File.Move(temporary, PathName);
    }
    public static bool IsToday(Todo t, DateTime today) => t.Due.HasValue && t.Due.Value.Date <= today.Date;
}
public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Contains("--self-test")) return SelfTest();
        bool smokeTest = args.Contains("--ui-smoke");
        bool uiTest = args.Contains("--ui-test") || smokeTest;
        using var mutex = new Mutex(true, smokeTest ? "Local\\SideTodo.Smoke" : uiTest ? "Local\\SideTodo.UITest" : "Local\\SideTodo.Desktop", out bool first);
        if (!first) { MessageBox.Show("SideTodo가 이미 실행 중입니다. 화면 왼쪽의 바 또는 작업 표시줄의 트레이 아이콘을 확인하세요.", "SideTodo"); return 0; }
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var store = new Store(uiTest ? System.IO.Path.Combine(System.IO.Path.GetTempPath(), "SideTodo-UI-" + Guid.NewGuid(), "tasks.json") : System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SideTodo", "tasks.json"));
        try { store.Load(); }
        catch (Exception e) { MessageBox.Show("할 일 파일을 읽지 못했습니다. 기존 파일을 보호하기 위해 앱을 종료합니다.\n" + store.PathName + "\n" + e.Message, "SideTodo"); return 1; }
        var widget = new Widget(store, args.Contains("--ui-test"));
        if (args.Contains("--preview") || uiTest) widget.Loaded += (_, _) => widget.Expand(true);
        if (smokeTest) widget.Loaded += async (_, _) => await widget.RunSmokeTest();
        return app.Run(widget);
    }
    static int SelfTest()
    {
        string directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "SideTodo-test-" + Guid.NewGuid());
        try
        {
            var store = new Store(System.IO.Path.Combine(directory, "tasks.json"));
            store.Load();
            var today = DateTime.Today;
            store.Data.Tasks.AddRange(new[] { new Todo { Title = "지난 할 일", Due = today.AddDays(-1) }, new Todo { Title = "오늘", Due = today }, new Todo { Title = "내일", Due = today.AddDays(1) }, new Todo { Title = "언젠가", Notes = "한국어 저장 확인" } });
            store.Save();
            CompletionArchive.SetDone(store.Data.Tasks[1], true);
            store.Save();
            var reload = new Store(store.PathName); reload.Load();
            if (reload.Data.Tasks.Count != 4 || !reload.Data.Tasks[1].Done || reload.Data.Tasks[3].Notes != "한국어 저장 확인") throw new Exception("Persistence failed");
            if (reload.Data.Tasks.Count(t => Store.IsToday(t, today)) != 2) throw new Exception("Date grouping failed");
            if (!File.Exists(store.PathName + ".bak")) throw new Exception("Backup failed");
            if (reload.Data.Tasks[1].CompletedAt == null) throw new Exception("Completion time lost on reload");
            var earlier = new Todo { Title = "먼저 완료", Notes = "한글 메모\n둘째 줄", Done = true, Due = today, CompletedAt = DateTimeOffset.Now.AddMinutes(-10) };
            var later = new Todo { Title = "최근 완료", Done = true, CompletedAt = DateTimeOffset.Now };
            var legacy = new Todo { Title = "이전 버전", Done = true };
            var pending = new Todo { Title = "미완료" };
            var archive = new[] { legacy, earlier, pending, later };
            if (CompletionArchive.Items(archive)[0] != later || CompletionArchive.Items(archive, false)[0] != earlier || CompletionArchive.Items(archive).Last() != legacy) throw new Exception("Archive order failed");
            using (var exported = JsonDocument.Parse(CompletionArchive.Json(archive)))
            {
                var items = exported.RootElement.GetProperty("tasks");
                if (items.GetArrayLength() != 3 || items[0].GetProperty("notes").GetString() != earlier.Notes || items[2].GetProperty("completedAt").ValueKind != JsonValueKind.Null) throw new Exception("JSON export failed");
            }
            if (!CompletionArchive.Markdown(archive).Contains(earlier.Notes)) throw new Exception("Markdown export lost memo");
            CompletionArchive.SetDone(earlier, false);
            if (earlier.CompletedAt != null || earlier.Done || earlier.Due != today || earlier.Notes != "한글 메모\n둘째 줄") throw new Exception("Restore failed");
            CompletionArchive.SetDone(earlier, true);
            if (earlier.CompletedAt == null || CompletionArchive.Items(archive)[0] != earlier) throw new Exception("Recompletion order failed");
            File.WriteAllText(store.PathName, "invalid json");
            try { reload.Load(); throw new Exception("Corruption was accepted"); } catch (JsonException) { }
            File.WriteAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "self-test-result.txt"), "PASS: save/reload, completion timestamps, newest/oldest ordering, legacy unknown times, JSON/Markdown export, restore, re-completion, Korean notes, date grouping, backup, corrupt-file protection");
            return 0;
        }
        catch (Exception e) { File.WriteAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "self-test-result.txt"), e.ToString()); return 1; }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}

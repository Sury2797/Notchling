using System.Text.Json;
using Notch.Core;

namespace Notch.Core.Tests;

internal static class WorkspaceCases
{
    private sealed record Notebook(SavedNote[] Notes, string Scratchpad);
    private static SavedNote Note(string text, int index = 0) =>
        new(new Guid(index + 1, 0, 0, new byte[8]), "Boundary fixture", text, new(2026, 10, 4, 0, 0, 0, TimeSpan.Zero));

    public static void Register(TestSuite suite)
    {
        suite.Add("Note and title limits accept the exact boundary and reject the complete oversized draft", () =>
        {
            var accepted = new string('x', WorkspaceLimits.MaximumTextLength);
            WorkspaceLimits.RequireText(accepted, WorkspaceLimits.MaximumTextLength, "Note");
            WorkspaceLimits.RequireText(new string('t', WorkspaceLimits.MaximumTitleLength), WorkspaceLimits.MaximumTitleLength, "Title");
            var oversized = accepted + "End of the complete draft";
            var failure = Check.Throws<ArgumentException>(() => WorkspaceLimits.RequireText(oversized, WorkspaceLimits.MaximumTextLength, "Note"));
            Check.True(failure.Message.Contains("full draft is retained"));
            Check.True(oversized.EndsWith("End of the complete draft", StringComparison.Ordinal));
            Check.Equal(WorkspaceLimits.MaximumTextLength + 25, oversized.Length);
            Check.Throws<ArgumentException>(() => WorkspaceLimits.RequireText(new string('t', WorkspaceLimits.MaximumTitleLength + 1), WorkspaceLimits.MaximumTitleLength, "Title"));
        });
        suite.Add("Notebook budget counts Unicode JSON bytes rather than character count", () =>
        {
            var ascii = new Notebook(Enumerable.Range(0, 20).Select(index => Note(new string('x', 100_000), index)).ToArray(), "");
            var unicode = new Notebook(Enumerable.Range(0, 20).Select(index => Note(new string('界', 100_000), index)).ToArray(), "");
            Check.Equal(ascii.Notes.Sum(note => note.Text.Length), unicode.Notes.Sum(note => note.Text.Length));
            WorkspaceLimits.RequireStorageBudget(ascii);
            var failure = Check.Throws<InvalidDataException>(() => WorkspaceLimits.RequireStorageBudget(unicode));
            Check.True(failure.Message.Contains("previous saved data are preserved"));
            Check.Equal('界', unicode.Notes[^1].Text[^1]);
            Check.Equal(100_000, unicode.Notes[^1].Text.Length);
        });
        suite.Add("Notebook budget accepts exactly 10 MB of serialized data and rejects one additional byte", () =>
        {
            var notes = Enumerable.Range(0, 20).Select(index => Note(new string('x', WorkspaceLimits.MaximumTextLength), index)).ToArray();
            var empty = new Notebook(notes, "");
            var remaining = checked((int)(LocalStore.MaximumBytes - WorkspaceLimits.SerializeExport(empty).LongLength));
            Check.True(remaining > 0 && remaining <= WorkspaceLimits.MaximumTextLength, "Fixture must obey text and item limits.");
            var exact = empty with { Scratchpad = new string('s', remaining) };
            Check.Equal(LocalStore.MaximumBytes, WorkspaceLimits.SerializeExport(exact).LongLength);
            WorkspaceLimits.RequireStorageBudget(exact);
            Check.Throws<InvalidDataException>(() => WorkspaceLimits.RequireStorageBudget(exact with { Scratchpad = exact.Scratchpad + "s" }));
            Check.Equal(remaining, exact.Scratchpad.Length);
        });
        suite.Add("Notebook budget includes escape expansion for otherwise valid full-length notes", () =>
        {
            var notes = Enumerable.Range(0, 20).Select(index => Note(new string('"', 300_000), index)).ToArray();
            foreach (var note in notes) WorkspaceLimits.RequireText(note.Text, WorkspaceLimits.MaximumTextLength, "Note");
            Check.True(notes.Sum(note => note.Text.Length) < LocalStore.MaximumBytes);
            Check.Throws<InvalidDataException>(() => WorkspaceLimits.RequireStorageBudget(new Notebook(notes, "")));
            Check.Equal(300_000, notes[^1].Text.Length);
        });
        suite.Add("Notebook export preserves Unicode, whitespace, empty text and the entire full-length note", () =>
        {
            const string prefix = "Line one\n界 🫶 café\r\n\t";
            var body = prefix + new string('z', WorkspaceLimits.MaximumTextLength - prefix.Length);
            var notebook = new Notebook([Note(body), Note("", 1)], "  Personal scratchpad\n");
            var bytes = WorkspaceLimits.SerializeExport(notebook);
            using var document = JsonDocument.Parse(bytes);
            var exportedNotes = document.RootElement.GetProperty("Notes");
            Check.Equal(body, exportedNotes[0].GetProperty("Text").GetString());
            Check.Equal("", exportedNotes[1].GetProperty("Text").GetString());
            Check.Equal(notebook.Scratchpad, document.RootElement.GetProperty("Scratchpad").GetString());
            WorkspaceLimits.RequireStorageBudget(notebook);
        });
    }
}

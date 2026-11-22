using System;
using System.IO;
using System.Threading;
using Godot;
using KAONOMI.Chat.Services;
using KAONOMI.Chat.ViewModels;
using KAONOMI.ChatGodot.Eve;

namespace KAONOMI.ChatGodot.Tools.Media;

/// <summary>
/// MY VOICE, a window of /recorder: the data to make a voice from one's own (<see cref="VoiceDatasetViewModel"/>).
/// At the top the name of the voice (📁 its folder) and the microphone (which one, its level); the VU meter and the
/// spectrum of what it hears; in the middle the sentence to read, large; under it ● RECORD (■ STOP while it records;
/// the space bar does both), REDO, PLAY the take, ‹ PREVIOUS and NEXT › (also ← and →), NEXT MISSING »; then what Whisper
/// heard of the take and how alike it is, and how many sentences and minutes are recorded.
/// </summary>
public static partial class MyVoiceView
{
    private const string WindowKey = "myvoice";

    public static void Open(RecorderWindowViewModel recorder)
    {
        var shell = Shell.Current;
        if (shell.TryBringBack(WindowKey))
            return;
        var vm = new VoiceDatasetViewModel(recorder, recorder.SpeechModel, recorder.Language);

        var name = Ui.Line(vm, nameof(vm.VoiceName), "The name of the voice (its folder)");
        name.CustomMinimumSize = new Vector2(220, 0);
        var folder = Ui.Action("FOLDER", () => OS.ShellOpen(vm.Folder), tooltip: "Open the folder of the data of the voice");
        var top = Ui.Toolbar(Ui.Row(10, Ui.Field("Voice", name), folder, Ui.Spring(), RecorderView.MicrophoneControls(recorder)));

        var scope = new RecorderView.Scope(recorder)
        {
            LiveOnly = true,
            CustomMinimumSize = new Vector2(0, 150),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            TooltipText = "What the microphone hears: keep the voice in the cyan and amber of the VU meter, never in the red (-3 dB: it clips)"
        };

        var number = Ui.Label(vm, nameof(vm.SentenceNumber), Eve.Eve.Muted, 13);
        var sentence = Ui.Label(vm, nameof(vm.Sentence), Eve.Eve.TextBright, 34, wrap: true);
        sentence.HorizontalAlignment = HorizontalAlignment.Center;
        sentence.VerticalAlignment = VerticalAlignment.Center;
        sentence.CustomMinimumSize = new Vector2(0, 150);
        sentence.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        var card = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        card.AddThemeStyleboxOverride("panel", Eve.Eve.Box(Eve.Eve.Sunken(0.25f), Eve.Eve.Edge with { A = 0.3f }, 8));
        card.AddChild(Ui.Margin(Ui.Column(6, number, sentence), 18, 12, 18, 12));
        Bind.When(card, vm, () => card.AddThemeStyleboxOverride("panel", vm.IsRecording
            ? Eve.Eve.Box(Eve.Eve.Red with { A = 0.12f }, Eve.Eve.Red, 8)
            : Eve.Eve.Box(Eve.Eve.Sunken(0.25f), Eve.Eve.Edge with { A = 0.3f }, 8)), nameof(vm.IsRecording));

        var record = Ui.Action("● RECORD", () => Toggle(vm), primary: true, tooltip: "Record the sentence (space bar): it stops by itself after a second of silence");
        record.CustomMinimumSize = new Vector2(150, 44);
        Bind.When(record, vm, () =>
        {
            record.Text = vm.IsRecording ? $"■ STOP  {vm.RecordingTime.TrimStart('●', ' ')}" : vm.IsChecking ? "… checking" : vm.Take is null ? "● RECORD" : "● REDO";
            record.Disabled = vm.IsChecking;
            record.Modulate = vm.IsRecording ? new Color(1f, 0.5f, 0.5f) : Colors.White;
        }, nameof(vm.IsRecording), nameof(vm.IsChecking), nameof(vm.Take), nameof(vm.Index), nameof(vm.RecordingTime));
        var playing = new CancellationTokenSource[1];
        var play = Ui.Action("PLAY", () =>
        {
            if (vm.Take is not { } take)
                return;
            playing[0]?.Cancel();
            playing[0] = new CancellationTokenSource();
            _ = AudioDevices.PlayAsync(Path.Combine(vm.Folder, "wavs", take.Id + ".wav"), playing[0].Token);
        }, tooltip: "Listen to the take of this sentence");
        Bind.When(play, vm, () => play.Disabled = vm.Take is null || vm.IsRecording, nameof(vm.Take), nameof(vm.Index), nameof(vm.IsRecording));
        var buttons = Ui.Row(10,
            Ui.Spring(),
            Ui.Action("‹ PREVIOUS", vm.Previous, tooltip: "The sentence before"),
            record, play,
            Ui.Action("NEXT ›", vm.Next, tooltip: "The next sentence (the take of this one is kept as it is)"),
            Ui.Action("NEXT MISSING »", vm.NextMissing, tooltip: "The next sentence not recorded yet"),
            Ui.Action("NEXT DOUBTFUL", vm.NextDoubtful, tooltip: "The next sentence whose take is doubtful (Whisper heard other words): to listen to it and record it again"),
            Ui.Action("+ SENTENCES", () => vm.AddSentences(), tooltip: "Add 100 new sentences to read (longer ones), at the end of sentences.txt: 30 to 60 minutes of voice make a good voice"),
            Ui.Spring());

        var take = Eve.Eve.Text_("", Eve.Eve.Muted, 13, wrap: true);
        void ShowTake()
        {
            if (vm.Take is not { } t)
            {
                take.Text = "Not recorded yet.";
                take.AddThemeColorOverride("font_color", Eve.Eve.Muted);
                return;
            }
            var good = t.Score >= VoiceDatasetViewModel.GoodScore;
            take.Text = $"{(good ? "✓" : "⚠")} take {t.Id}: {t.Seconds:0.0} s · {t.Score * 100:0} % alike · Whisper heard « {(t.Heard.Length > 0 ? t.Heard : "nothing")} »";
            take.AddThemeColorOverride("font_color", good ? Eve.Eve.Green : Eve.Eve.Amber);
        }
        ShowTake();
        Bind.When(take, vm, ShowTake, nameof(vm.Take), nameof(vm.Index));
        var state = Ui.Label(vm, nameof(vm.State), Eve.Eve.Text, 13, wrap: true);
        Bind.When(state, vm, () => state.AddThemeColorOverride("font_color", vm.IsWarning ? Eve.Eve.Amber : Eve.Eve.Text), nameof(vm.IsWarning));
        var progress = Ui.Label(vm, nameof(vm.Progress), Eve.Eve.Cyan, 13);

        var page = Ui.Column(10, top, scope, card, buttons, Ui.Margin(Ui.Column(6, take, state, progress), 12, 0, 12, 8));
        // The space bar records and stops, wherever the focus is in this window (but in a text).
        var keys = new KeyCatcher(page, () => Toggle(vm), vm.Previous, vm.Next);
        page.AddChild(keys);
        // The recorder kept open meanwhile (its microphone is the one heard here); closed, this window closes too.
        recorder.VoiceWindow(true);
        Action closeWith = () => Bind.OnMain(() =>
        {
            if (GodotObject.IsInstanceValid(page))
                shell.Close(page);
        });
        recorder.Disposed += closeWith;
        Bind.OnGone(page, () =>
        {
            recorder.Disposed -= closeWith;
            recorder.VoiceWindow(false);
            vm.Dispose();
        });
        // Opened with its view model: it says the window stays open (see VoiceDatasetViewModel.StaysOpen).
        shell.Deck.Open(WindowKey, "My voice", "🗣", page, new Vector2(1200, 820), vm);
    }

    private static void Toggle(VoiceDatasetViewModel vm)
    {
        if (vm.IsRecording)
            vm.Stop();
        else
            vm.Record();
    }

    /// <summary>The space bar (record, stop), ← and → (the sentence before, the next one), while the focus is in the window and not in a text.</summary>
    private sealed partial class KeyCatcher(Control page, Action toggle, Action previous, Action next) : Node
    {
        public KeyCatcher() : this(null!, () => { }, () => { }, () => { })
        {
        }

        public override void _Input(InputEvent @event)
        {
            if (@event is not InputEventKey { Pressed: true, Echo: false } key || key.Keycode is not (Key.Space or Key.Left or Key.Right) || page is null || !page.IsVisibleInTree())
                return;
            var focus = page.GetViewport()?.GuiGetFocusOwner();
            if (focus is LineEdit or TextEdit || (focus is not null && !page.IsAncestorOf(focus)))
                return;
            (key.Keycode == Key.Space ? toggle : key.Keycode == Key.Left ? previous : next)();
            page.GetViewport()?.SetInputAsHandled();
        }
    }
}

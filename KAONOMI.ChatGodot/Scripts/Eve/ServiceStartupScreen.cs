using System;
using Godot;
using KAONOMI.Chat.ViewModels;

namespace KAONOMI.ChatGodot.Eve;

/// <summary>
/// The popup of the start, between the login and the chat: the services the chat needs
/// (<see cref="ServiceStartupViewModel"/>: the search engine, the Mongo engine, the LLM gateway, the graph database,
/// the server),
/// one line each with a dot (grey: not looked at yet; amber: checked or starting; green: it answers; red: it does not
/// run) and what is being done. Everything running, it closes by itself and the chat opens. A service that does not run
/// leaves it open with RETRY and OPEN THE CHAT, which is pressed by itself after <see cref="FailedWait"/> seconds (a
/// chat started with nobody in front of it still opens). OPEN THE CHAT NOW does not wait: the services go on starting
/// behind the chat.
/// </summary>
public partial class ServiceStartupScreen : Control
{
    /// <summary>The popup stays this long once everything runs (seconds): read, not just a flash; longer when a service was started.</summary>
    private const double RunningHold = 0.7, StartedHold = 1.6;

    /// <summary>With a service not running, the chat opens by itself after so many seconds.</summary>
    private const double FailedWait = 20;

    private readonly ServiceStartupViewModel? _vm;
    private readonly Action? _proceed;
    private readonly Button _open = Eve.Button("OPEN THE CHAT NOW", tooltip: "Open the chat without waiting: the services go on starting behind it (/process shows them)");
    private readonly Button _retry = Eve.Button("↻ RETRY", tooltip: "Check the services again, and start those that do not run");
    private readonly ProgressBar _bar = new() { Indeterminate = true, ShowPercentage = false, CustomMinimumSize = new Vector2(0, 4) };
    private double _countdown = -1;
    private bool _left;

    // Godot builds its scripts with this constructor.
    public ServiceStartupScreen()
    {
    }

    /// <param name="vm">The services to check; the screen runs it.</param>
    /// <param name="proceed">Called once, when the chat is to open (everything runs, or the user did not wait).</param>
    public ServiceStartupScreen(ServiceStartupViewModel vm, Action proceed)
    {
        _vm = vm;
        _proceed = proceed;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        var shade = new ColorRect { Color = Eve.Space, MouseFilter = MouseFilterEnum.Ignore };
        shade.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(shade);

        var title = Eve.Caption("KAONOMI", Eve.TextBright, 26);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        var subtitle = Eve.Text_("The services of the chat are checked, and started when they do not run", Eve.Muted, 12);
        subtitle.HorizontalAlignment = HorizontalAlignment.Center;

        var rows = new VBoxContainer();
        rows.AddThemeConstantOverride("separation", 6);
        foreach (var step in vm.Steps)
            rows.AddChild(Row(step));

        var status = Ui.Label(vm, nameof(vm.Status), Eve.Text, 12, wrap: true);
        status.HorizontalAlignment = HorizontalAlignment.Center;
        status.CustomMinimumSize = new Vector2(560, 0);

        _open.Pressed += Leave;
        _retry.Pressed += Retry;
        _retry.Visible = false;
        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        buttons.AddThemeConstantOverride("separation", 10);
        buttons.AddChild(_retry);
        buttons.AddChild(_open);

        var column = Ui.Column(12, title, subtitle, new Control { CustomMinimumSize = new Vector2(0, 4) }, rows, _bar, status, buttons);
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(620, 0) };
        panel.AddThemeStyleboxOverride("panel", Eve.Box(Eve.PanelSolid, Eve.Edge, 28, 1, Eve.FloatCorner));
        panel.AddChild(column);
        var center = new CenterContainer();
        center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        center.AddChild(panel);
        AddChild(center);
    }

    public override void _Ready()
    {
        if (_vm is null)
            return;
        Bind.When(this, _vm, Done, nameof(_vm.IsDone));
        _ = _vm.RunAsync();
    }

    /// <summary>A service: its dot, its name, its address, what is done of it.</summary>
    private Control Row(ServiceStep step)
    {
        var dot = new ColorRect { CustomMinimumSize = new Vector2(12, 12), SizeFlagsVertical = SizeFlags.ShrinkCenter };
        var name = Eve.Text_(step.Name, Eve.TextBright, 14);
        name.AddThemeFontOverride("font", Eve.Display);
        name.CustomMinimumSize = new Vector2(140, 0);
        name.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        var detail = Ui.Label(step, nameof(step.Detail), Eve.Muted, 11, wrap: true);
        detail.AddThemeFontOverride("font", Eve.Mono);
        detail.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        detail.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        detail.CustomMinimumSize = new Vector2(380, 0);

        var panel = new PanelContainer { TooltipText = step.Process.Description };
        panel.AddThemeStyleboxOverride("panel", Eve.Box(Eve.Field, Eve.Edge with { A = 0.4f }, 8));
        panel.AddChild(Ui.Margin(Ui.Row(12, dot, name, detail), 10, 6, 10, 6));

        void Look()
        {
            dot.Color = step.State switch
            {
                ServiceState.Waiting => Eve.Muted with { A = 0.35f },
                ServiceState.Running or ServiceState.Started => Eve.Green,
                ServiceState.Failed => Eve.Red,
                _ => Eve.Amber
            };
            detail.AddThemeColorOverride("font_color", step.State == ServiceState.Failed ? Eve.Red : step.State == ServiceState.Waiting ? Eve.Muted with { A = 0.6f } : Eve.Muted);
        }
        Bind.When(panel, step, Look, nameof(step.State));
        return panel;
    }

    /// <summary>Every service looked at: the chat opens after a moment, or the screen waits when one does not run.</summary>
    private void Done()
    {
        if (_vm is null || _left)
            return;
        _bar.Visible = !_vm.IsDone;
        if (!_vm.IsDone)
        {
            _retry.Visible = false;
            _countdown = -1;
            _open.Text = "OPEN THE CHAT NOW";
            return;
        }
        if (_vm.HasFailed)
        {
            _retry.Visible = true;
            _countdown = FailedWait;
            _open.Text = $"OPEN THE CHAT ({(int)FailedWait})";
            _open.GrabFocus();
            return;
        }
        _open.Visible = false;
        GetTree().CreateTimer(_vm.HasStarted ? StartedHold : RunningHold).Timeout += Leave;
    }

    public override void _Process(double delta)
    {
        if (_countdown < 0 || _left)
            return;
        _countdown -= delta;
        if (_countdown <= 0)
        {
            Leave();
            return;
        }
        _open.Text = $"OPEN THE CHAT ({(int)Math.Ceiling(_countdown)})";
    }

    private void Retry()
    {
        if (_vm is null || _left || !_vm.IsDone)
            return;
        _countdown = -1;
        _ = _vm.RunAsync();
    }

    /// <summary>The chat opens (once): the screen goes.</summary>
    private void Leave()
    {
        if (_left || !IsInstanceValid(this))
            return;
        _left = true;
        _countdown = -1;
        QueueFree();
        _proceed?.Invoke();
    }
}

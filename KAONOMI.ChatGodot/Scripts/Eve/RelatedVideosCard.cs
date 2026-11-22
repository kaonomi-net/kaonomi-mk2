using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Godot;
using KAONOMI.Chat.Models;
using KAONOMI.Chat.Services;

namespace KAONOMI.ChatGodot.Eve;

/// <summary>
/// The YouTube videos of Kaonomi that share entities with the answer just shown: a small card floating beside the 3D
/// animation (on its left, else on its right), 30 % transparent (opaque under the mouse), following the animation
/// when it moves. Each video: its thumbnail, its title, the entities it shares; a click opens it in the browser. ✕
/// closes the card, and it closes by itself after <see cref="Life"/> seconds (the thin bar at its bottom shows the
/// time left). A new answer with videos replaces it.
/// </summary>
public partial class RelatedVideosCard : PanelContainer
{
    /// <summary>How long the card stays, in seconds.</summary>
    public const double Life = 30;

    private const float Opacity = 0.7f;
    private const float Width = 340;

    private static RelatedVideosCard? _shown;

    private readonly Func<Rect2?> _anchor;
    private readonly CancellationTokenSource _cancel = new();
    private ColorRect _bar = null!;
    private double _left = Life;
    private bool _closing;
    private bool _hover;

    private RelatedVideosCard(IReadOnlyList<RelatedVideo> videos, Func<Rect2?> anchor)
    {
        _anchor = anchor;
        Name = nameof(RelatedVideosCard);
        ZIndex = 100;
        CustomMinimumSize = new Vector2(Width, 0);
        Modulate = new Color(1, 1, 1, 0);
        AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = Eve.PanelSolid,
            BorderColor = Eve.EdgeBright,
            BorderWidthBottom = 1, BorderWidthTop = 1, BorderWidthLeft = 1, BorderWidthRight = 1,
            CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6, CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6,
            ContentMarginLeft = 10, ContentMarginRight = 10, ContentMarginTop = 8, ContentMarginBottom = 6,
            ShadowColor = new Color(0, 0, 0, 0.35f), ShadowSize = 8,
        });

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 8);
        AddChild(column);

        var header = new HBoxContainer();
        var title = Eve.Text_("YOUTUBE  ·  ON THE SAME SUBJECT", Eve.Cyan, 11);
        title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        title.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        header.AddChild(title);
        var close = new Button { Text = "✕", Flat = true, TooltipText = "Close", FocusMode = FocusModeEnum.None, MouseDefaultCursorShape = CursorShape.PointingHand };
        close.Pressed += Close;
        header.AddChild(close);
        column.AddChild(header);

        foreach (var related in videos)
            column.AddChild(Row(related));

        // The time left.
        var track = new Control { CustomMinimumSize = new Vector2(0, 2), MouseFilter = MouseFilterEnum.Ignore };
        _bar = new ColorRect { Color = Eve.Cyan, MouseFilter = MouseFilterEnum.Ignore, AnchorBottom = 1, AnchorRight = 1 };
        track.AddChild(_bar);
        column.AddChild(track);

        MinimumSizeChanged += () => _resized = true;
        MouseEntered += () => SetHover(true);
        MouseExited += () => SetHover(!GetGlobalRect().HasPoint(GetGlobalMousePosition()) ? false : _hover);
    }

    /// <summary>
    /// Shows <paramref name="videos"/> beside the rectangle <paramref name="anchor"/> gives (the 3D animation, on the
    /// screen; asked again at each frame), in place of the card shown.
    /// </summary>
    public static void Show(IReadOnlyList<RelatedVideo> videos, Func<Rect2?> anchor)
    {
        if (videos.Count == 0)
            return;
        if (_shown is { } old && IsInstanceValid(old))
            old.Remove();
        var card = new RelatedVideosCard(videos, anchor);
        _shown = card;
        Shell.Current.Root.AddChild(card);
        card.Place();
        card.CreateTween().TweenProperty(card, "modulate:a", Opacity, 0.4);
    }

    private Control Row(RelatedVideo related)
    {
        var video = related.Video;
        var row = new PanelContainer { MouseDefaultCursorShape = CursorShape.PointingHand, TooltipText = $"{video.Title}\n{video.Url}\n\nClick: open it in the browser" };
        var normal = new StyleBoxFlat { BgColor = Colors.Transparent, CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4, CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4 };
        var hot = new StyleBoxFlat { BgColor = Eve.Raised(0.08f), CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4, CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4 };
        row.AddThemeStyleboxOverride("panel", normal);
        row.MouseEntered += () => { row.AddThemeStyleboxOverride("panel", hot); SetHover(true); };
        row.MouseExited += () => row.AddThemeStyleboxOverride("panel", normal);
        row.GuiInput += e =>
        {
            if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
            {
                OS.ShellOpen(video.Url is { Length: > 0 } url ? url : YoutubeInfo.WatchUrl(video.VideoId!));
                row.AcceptEvent();
            }
        };

        var line = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        line.AddThemeConstantOverride("separation", 10);
        row.AddChild(line);
        var thumb = new TextureRect
        {
            CustomMinimumSize = new Vector2(128, 72),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
            MouseFilter = MouseFilterEnum.Ignore,
            ClipContents = true,
        };
        var back = new ColorRect { Color = Eve.Sunken(0.5f), MouseFilter = MouseFilterEnum.Ignore, ShowBehindParent = true };
        back.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        thumb.AddChild(back);
        line.AddChild(thumb);
        LoadThumbnail(thumb, video.VideoId!);

        var text = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };
        text.AddThemeConstantOverride("separation", 2);
        var name = Eve.Text_(string.IsNullOrWhiteSpace(video.Title) ? video.VideoId! : video.Title!, Eve.TextBright, 13, wrap: true);
        name.MaxLinesVisible = 3;
        name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        name.MouseFilter = MouseFilterEnum.Ignore;
        text.AddChild(name);
        var shared = Eve.Text_(string.Join(" · ", related.Entities), Eve.Amber, 11, wrap: true);
        shared.MaxLinesVisible = 2;
        shared.MouseFilter = MouseFilterEnum.Ignore;
        text.AddChild(shared);
        line.AddChild(text);
        return row;
    }

    private async void LoadThumbnail(TextureRect thumb, string videoId)
    {
        try
        {
            if (await YoutubeInfo.ThumbnailAsync(videoId, _cancel.Token) is not { Length: > 0 } bytes)
                return;
            Bind.OnMain(() =>
            {
                if (!IsInstanceValid(thumb))
                    return;
                var image = new Image();
                if (image.LoadJpgFromBuffer(bytes) != Error.Ok && image.LoadPngFromBuffer(bytes) != Error.Ok && image.LoadWebpFromBuffer(bytes) != Error.Ok)
                    return;
                thumb.Texture = ImageTexture.CreateFromImage(image);
            });
        }
        catch (Exception ex) when (ex is OperationCanceledException or System.Net.Http.HttpRequestException)
        {
        }
    }

    private void SetHover(bool hover)
    {
        if (_closing || hover == _hover)
            return;
        _hover = hover;
        CreateTween().TweenProperty(this, "modulate:a", hover ? 1f : Opacity, 0.2);
    }

    public override void _Process(double delta)
    {
        if (_closing)
            return;
        _left -= delta;
        _bar.AnchorRight = (float)Math.Clamp(_left / Life, 0, 1);
        if (_hover && !GetGlobalRect().HasPoint(GetGlobalMousePosition()))
            SetHover(false);
        Place();
        if (_left <= 0)
            Close();
    }

    // Where it was placed last: for which place of the animation, in which window, at which size of its own.
    private Rect2 _placedBeside;
    private Vector2 _placedIn;
    private bool _resized = true;

    /// <summary>
    /// Beside the animation: on its left, else on its right, within the window. Asked at each frame; done again only
    /// when the animation moved, the window changed or the card got another size.
    /// </summary>
    private void Place()
    {
        var screen = GetViewportRect().Size;
        var rect = _anchor() ?? new Rect2(screen.X - 8, 60, 0, 0);
        if (!_resized && rect == _placedBeside && screen == _placedIn)
            return;
        (_placedBeside, _placedIn, _resized) = (rect, screen, false);
        var size = GetCombinedMinimumSize();
        Size = size;
        var x = rect.Position.X - size.X - 12;
        if (x < 8)
            x = Math.Min(rect.End.X + 12, screen.X - size.X - 8);
        var y = Math.Clamp(rect.Position.Y, 8, Math.Max(8, screen.Y - size.Y - 8));
        Position = new Vector2(x, y).Round();
    }

    private void Close()
    {
        if (_closing)
            return;
        _closing = true;
        var tween = CreateTween();
        tween.TweenProperty(this, "modulate:a", 0f, 0.5);
        tween.TweenCallback(Callable.From(Remove));
    }

    private void Remove()
    {
        _cancel.Cancel();
        if (_shown == this)
            _shown = null;
        QueueFree();
    }
}

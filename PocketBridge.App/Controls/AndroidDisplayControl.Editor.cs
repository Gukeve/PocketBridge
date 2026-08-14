using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using PocketBridge.Core.Models;

namespace PocketBridge.App.Controls;

public partial class AndroidDisplayControl
{
    private readonly Stack<InputProfile> _undo = new();
    private readonly Stack<InputProfile> _redo = new();
    private int _selectedBinding = -1;
    private Point? _dragStart;
    private InputAction? _dragOriginal;
    private bool _resizeDrag;
    private NormalizedPoint? _createStart;

    private void InitializeEditor()
    {
        EditToolBox.ItemsSource = new[] { InputActionKind.TouchPoint, InputActionKind.TouchRegion, InputActionKind.Swipe, InputActionKind.VirtualJoystick, InputActionKind.FreeLook };
        EditToolBox.SelectedItem = InputActionKind.TouchPoint;
        MappingOverlay.MouseMove += EditorCanvasMove; MappingOverlay.MouseLeftButtonUp += EditorCanvasUp;
    }

    private VisibleVideoRect VideoRect()
    {
        if (Session is not { VideoWidth: > 0, VideoHeight: > 0 }) return new(0, 0, 0, 0);
        var origin = VideoImage.TranslatePoint(new Point(), this);
        return InputCoordinateMapper.Fit(VideoImage.ActualWidth, VideoImage.ActualHeight, Session.VideoWidth, Session.VideoHeight, origin.X, origin.Y);
    }

    private NormalizedPoint? Normalized(Point point) => VideoRect().ToNormalized(point.X, point.Y);
    private double Snap(double value) => SnapBox.IsChecked == true ? Math.Round(value / .02) * .02 : value;
    private NormalizedPoint SnapPoint(NormalizedPoint value) => new NormalizedPoint(Snap(value.X), Snap(value.Y)).Clamp();

    private void EditorRefresh()
    {
        MappingOverlay.Visibility = EditorToolbar.Visibility = IsMappingEditMode ? Visibility.Visible : Visibility.Collapsed;
        MappingOverlay.Children.Clear();
        if (!IsMappingEditMode || MappingProfile is null || VideoRect() is not { Width: > 0, Height: > 0 } rect) return;
        for (var index = 0; index < MappingProfile.Bindings.Count; index++) RenderBinding(index, MappingProfile.Bindings[index], rect);
    }

    private void RenderBinding(int index, PocketBridge.Core.Models.KeyBinding binding, VisibleVideoRect rect)
    {
        var action = binding.Action.Normalize(); var selected = index == _selectedBinding; var accent = selected ? Color.FromRgb(96, 165, 250) : Color.FromRgb(82, 106, 158);
        if (action.Kind is InputActionKind.TouchRegion or InputActionKind.FreeLook && action.Region is { } region)
        {
            var start = rect.ToControl(region.Start); var end = rect.ToControl(region.End); var border = EditorBorder(index, binding.Input, accent, action.Kind == InputActionKind.FreeLook ? new DoubleCollection { 5, 3 } : null);
            border.Width = Math.Max(24, end.X - start.X); border.Height = Math.Max(24, end.Y - start.Y); Canvas.SetLeft(border, start.X); Canvas.SetTop(border, start.Y); MappingOverlay.Children.Add(border); AddResizeHandle(index, end.X, end.Y, accent); return;
        }
        if (action.Kind == InputActionKind.Swipe && action.Region is { } swipe)
        {
            var start = rect.ToControl(swipe.Start); var end = rect.ToControl(swipe.End); var line = new Line { X1 = start.X, Y1 = start.Y, X2 = end.X, Y2 = end.Y, Stroke = new SolidColorBrush(accent), StrokeThickness = selected ? 5 : 3, Tag = index, ToolTip = $"{binding.Input} · {action.DurationMs} ms" }; HookDrag(line, false); MappingOverlay.Children.Add(line);
            AddEndpoint(index, start.X, start.Y, accent, false); AddEndpoint(index, end.X, end.Y, accent, true); return;
        }
        var point = action.Point ?? new NormalizedPoint(.5, .5); var center = rect.ToControl(point); var diameter = action.Kind == InputActionKind.VirtualJoystick ? Math.Max(48, action.Radius * 2 * Math.Min(rect.Width, rect.Height)) : 46;
        var marker = EditorBorder(index, binding.Input, accent, action.Kind == InputActionKind.VirtualJoystick ? new DoubleCollection { 3, 3 } : null); marker.Width = marker.Height = diameter; marker.CornerRadius = new CornerRadius(diameter / 2); Canvas.SetLeft(marker, center.X - diameter / 2); Canvas.SetTop(marker, center.Y - diameter / 2); MappingOverlay.Children.Add(marker);
        if (action.Kind == InputActionKind.VirtualJoystick) { var dead = new Ellipse { Width = diameter * binding.DeadZone, Height = diameter * binding.DeadZone, Stroke = Brushes.White, StrokeThickness = 1, IsHitTestVisible = false }; Canvas.SetLeft(dead, center.X - dead.Width / 2); Canvas.SetTop(dead, center.Y - dead.Height / 2); MappingOverlay.Children.Add(dead); AddResizeHandle(index, center.X + diameter / 2, center.Y + diameter / 2, accent); }
    }

    private Border EditorBorder(int index, string label, Color accent, DoubleCollection? dash)
    {
        var border = new Border { MinWidth = 24, MinHeight = 24, Background = new SolidColorBrush(Color.FromArgb(100, accent.R, accent.G, accent.B)), BorderBrush = new SolidColorBrush(accent), BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(7), Tag = index, ToolTip = label, Child = new TextBlock { Text = label, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontSize = 11, Margin = new Thickness(4), TextTrimming = TextTrimming.CharacterEllipsis } };
        HookDrag(border, false); return border;
    }

    private void AddResizeHandle(int index, double x, double y, Color color) { var handle = new Border { Width = 14, Height = 14, CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(color), BorderBrush = Brushes.White, BorderThickness = new Thickness(1), Tag = index, Cursor = Cursors.SizeNWSE }; HookDrag(handle, true); Canvas.SetLeft(handle, x - 7); Canvas.SetTop(handle, y - 7); MappingOverlay.Children.Add(handle); }
    private void AddEndpoint(int index, double x, double y, Color color, bool resize) { var endpoint = new Ellipse { Width = 16, Height = 16, Fill = new SolidColorBrush(color), Stroke = Brushes.White, StrokeThickness = 1, Tag = index, Cursor = Cursors.SizeAll }; HookDrag(endpoint, resize); Canvas.SetLeft(endpoint, x - 8); Canvas.SetTop(endpoint, y - 8); MappingOverlay.Children.Add(endpoint); }
    private void HookDrag(FrameworkElement element, bool resize) { element.MouseLeftButtonDown += (_, e) => { if (MappingProfile is null || element.Tag is not int index) return; _selectedBinding = index; _dragStart = e.GetPosition(this); _dragOriginal = MappingProfile.Bindings[index].Action; _resizeDrag = resize; element.CaptureMouse(); RefreshOverlay(); e.Handled = true; }; }

    private void EditorCanvasDown(MouseButtonEventArgs e)
    {
        if (!IsMappingEditMode || MappingProfile is null || Normalized(e.GetPosition(this)) is not { } point) return;
        Focus(); var tool = EditToolBox.SelectedItem is InputActionKind kind ? kind : InputActionKind.TouchPoint; point = SnapPoint(point);
        if (tool is InputActionKind.TouchPoint or InputActionKind.VirtualJoystick) { PushUndo(); AddBinding(tool, point, null); }
        else { _createStart = point; MappingOverlay.CaptureMouse(); }
        e.Handled = true;
    }

    private void EditorCanvasMove(object sender, MouseEventArgs e)
    {
        if (_dragStart is null || _dragOriginal is null || MappingProfile is null || _selectedBinding < 0 || Normalized(e.GetPosition(this)) is not { } current || Normalized(_dragStart.Value) is not { } start) return;
        var dx = current.X - start.X; var dy = current.Y - start.Y; var action = _dragOriginal;
        if (_resizeDrag && action.Kind == InputActionKind.VirtualJoystick && action.Point is { } center) action = action with { Radius = Math.Max(.02, Math.Sqrt(Math.Pow(current.X - center.X, 2) + Math.Pow(current.Y - center.Y, 2))) };
        else if (_resizeDrag && action.Region is { } region) action = action with { Region = new(region.Start, SnapPoint(current)) };
        else if (action.Point is { } point) action = action with { Point = SnapPoint(new(point.X + dx, point.Y + dy)) };
        else if (action.Region is { } movedRegion) action = action with { Region = new(SnapPoint(new NormalizedPoint(movedRegion.Start.X + dx, movedRegion.Start.Y + dy)), SnapPoint(new NormalizedPoint(movedRegion.End.X + dx, movedRegion.End.Y + dy))) };
        ReplaceSelected(action.Normalize(), false);
    }

    private void EditorCanvasUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragStart is not null) { PushUndo(_dragOriginal); CommitProfile(); _dragStart = null; _dragOriginal = null; MappingOverlay.ReleaseMouseCapture(); e.Handled = true; return; }
        if (_createStart is { } start && Normalized(e.GetPosition(this)) is { } end && EditToolBox.SelectedItem is InputActionKind tool) { PushUndo(); AddBinding(tool, start, new(start, SnapPoint(end))); }
        _createStart = null; MappingOverlay.ReleaseMouseCapture(); e.Handled = true;
    }

    private void AddBinding(InputActionKind kind, NormalizedPoint point, NormalizedRegion? region)
    {
        if (MappingProfile is null) return; var number = MappingProfile.Bindings.Count + 1; var action = new InputAction(kind, kind.ToString(), point, region).Normalize(); var binding = new PocketBridge.Core.Models.KeyBinding(InputSourceKind.KeyboardKey, $"Key{number}", action);
        SetCurrentValue(MappingProfileProperty, MappingProfile with { Bindings = MappingProfile.Bindings.Append(binding).ToArray() }); _selectedBinding = MappingProfile!.Bindings.Count - 1; CommitProfile();
    }

    private void ReplaceSelected(InputAction action, bool commit = true)
    {
        if (MappingProfile is null || _selectedBinding < 0 || _selectedBinding >= MappingProfile.Bindings.Count) return; var bindings = MappingProfile.Bindings.ToArray(); bindings[_selectedBinding] = bindings[_selectedBinding] with { Action = action }; SetCurrentValue(MappingProfileProperty, MappingProfile with { Bindings = bindings }); if (commit) CommitProfile(); else RefreshOverlay();
    }

    private void PushUndo(InputAction? original = null) { if (MappingProfile is null) return; if (original is null) _undo.Push(MappingProfile); else { var bindings = MappingProfile.Bindings.ToArray(); bindings[_selectedBinding] = bindings[_selectedBinding] with { Action = original }; _undo.Push(MappingProfile with { Bindings = bindings }); } _redo.Clear(); }
    private void CommitProfile() { if (MappingProfile is null) return; MappingProfileEdited?.Invoke(this, MappingProfile); RefreshOverlay(); }
    private void Undo_Click(object sender, RoutedEventArgs e) { if (MappingProfile is null || _undo.Count == 0) return; _redo.Push(MappingProfile); SetCurrentValue(MappingProfileProperty, _undo.Pop()); CommitProfile(); }
    private void Redo_Click(object sender, RoutedEventArgs e) { if (MappingProfile is null || _redo.Count == 0) return; _undo.Push(MappingProfile); SetCurrentValue(MappingProfileProperty, _redo.Pop()); CommitProfile(); }
    private void Duplicate_Click(object sender, RoutedEventArgs e) { if (MappingProfile is null || _selectedBinding < 0) return; PushUndo(); var source = MappingProfile.Bindings[_selectedBinding]; var action = source.Action.Point is { } p ? source.Action with { Point = new NormalizedPoint(p.X + .03, p.Y + .03).Clamp() } : source.Action; SetCurrentValue(MappingProfileProperty, MappingProfile with { Bindings = MappingProfile.Bindings.Append(source with { Input = source.Input + " copy", Action = action }).ToArray() }); _selectedBinding = MappingProfile!.Bindings.Count - 1; CommitProfile(); }
    private void Delete_Click(object sender, RoutedEventArgs e) { if (MappingProfile is null || _selectedBinding < 0) return; PushUndo(); SetCurrentValue(MappingProfileProperty, MappingProfile with { Bindings = MappingProfile.Bindings.Where((_, index) => index != _selectedBinding).ToArray() }); _selectedBinding = Math.Min(_selectedBinding, MappingProfile!.Bindings.Count - 1); CommitProfile(); }
    private void EditTool_Changed(object sender, SelectionChangedEventArgs e) { }

    private bool HandleEditorKey(KeyEventArgs e)
    {
        if (!IsMappingEditMode || MappingProfile is null) return false;
        if (e.Key == Key.Delete) { Delete_Click(this, new RoutedEventArgs()); return true; }
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key == Key.D) { Duplicate_Click(this, new RoutedEventArgs()); return true; }
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key == Key.Z) { Undo_Click(this, new RoutedEventArgs()); return true; }
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key == Key.Y) { Redo_Click(this, new RoutedEventArgs()); return true; }
        var delta = e.Key switch { Key.Left => (-.005, 0d), Key.Right => (.005, 0d), Key.Up => (0d, -.005), Key.Down => (0d, .005), _ => (0d, 0d) }; if (delta == (0d, 0d) || _selectedBinding < 0) return false;
        PushUndo(); var action = MappingProfile.Bindings[_selectedBinding].Action; if (action.Point is { } p) action = action with { Point = new NormalizedPoint(p.X + delta.Item1, p.Y + delta.Item2).Clamp() }; else if (action.Region is { } r) action = action with { Region = new(new NormalizedPoint(r.Start.X + delta.Item1, r.Start.Y + delta.Item2).Clamp(), new NormalizedPoint(r.End.X + delta.Item1, r.End.Y + delta.Item2).Clamp()) }; ReplaceSelected(action.Normalize()); return true;
    }
}

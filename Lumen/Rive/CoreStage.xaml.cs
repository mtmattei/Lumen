using Lumen.Domain;
using Lumen.Presentation;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Lumen.Rive;

/// <summary>
/// Hosts the Core. Translates pointer, touch, keyboard and tilt into presenter inputs, and produces the
/// press events with C#-owned timing. Pointer movement never touches XAML layout.
/// </summary>
public sealed partial class CoreStage : UserControl
{
    private readonly PressGesture _press = new();
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _holdTimer;
    private double _gravityX;
    private double _gravityY;
    private double _pointerX;
    private double _pointerY;
    private double _pointerDistance = 1;
    private double _contactX;
    private double _contactY;
    private SubsystemId _keyboardSubsystem = SubsystemId.Power;

    public CoreStage()
    {
        InitializeComponent();

        Presenter = CreatePresenter();

        HitArea.PointerMoved += OnPointerMoved;
        HitArea.PointerPressed += OnPointerPressed;
        HitArea.PointerReleased += OnPointerReleased;
        HitArea.PointerCanceled += (_, _) => CancelPress();
        HitArea.PointerCaptureLost += (_, _) => { if (_press.Phase != PressPhase.Idle) Release(); };
        HitArea.PointerExited += (_, _) => { _pointerDistance = 1; PushInteraction(); };
        HitArea.DoubleTapped += (_, e) => { ExploreRequested?.Invoke(this, EventArgs.Empty); e.Handled = true; };
        KeyDown += OnKeyDown;
        KeyUp += OnKeyUp;
        Unloaded += (_, _) => _holdTimer?.Stop();
    }

    public ICoreStagePresenter Presenter { get; }

    /// <summary>
    /// Rive runtime when liblumen_rive and lumen-core.riv load; the procedural renderer otherwise
    /// (spec: if Rive fails, Uno status and controls remain usable). LUMEN_CORE=procedural forces the fallback.
    /// </summary>
    private ICoreStagePresenter CreatePresenter()
    {
        if (!LumenCoreCanvas.IsSupportedOnCurrentPlatform())
        {
            FallbackText.Visibility = Visibility.Visible;
            return new LumenCorePresenter(null, DispatcherQueue);
        }

        if (Environment.GetEnvironmentVariable("LUMEN_CORE") != "procedural" && TryLoadRive() is { } scene)
        {
            var riveCanvas = new RiveCoreCanvas();
            HitArea.Children.Insert(0, riveCanvas);
            return new RiveCorePresenter(scene, riveCanvas, DispatcherQueue);
        }

        var canvas = new LumenCoreCanvas();
        HitArea.Children.Insert(0, canvas);
        return new LumenCorePresenter(canvas, DispatcherQueue);
    }

    private static RiveScene? TryLoadRive()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Assets", "Rive", "lumen-core.riv");
            if (!RiveScene.IsRuntimeAvailable || !File.Exists(path)) return null;
            return RiveScene.Load(File.ReadAllBytes(path), "LumenCore", "LumenCore");
        }
        catch (Exception e) when (e is InvalidDataException or IOException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>Press/charge/release events, routed to <see cref="RiveEventAdapter"/> with the presenter's events.</summary>
    public event EventHandler<CoreEvent>? PressEventRaised;

    public event EventHandler? ExploreRequested;

    public event EventHandler? CollapseRequested;

    public void SetGravity(double x, double y)
    {
        _gravityX = x;
        _gravityY = y;
        PushInteraction();
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var p = e.GetCurrentPoint(HitArea).Position;
        Presenter.Pointer(CorePointer.Move, p.X, p.Y);
        var dx = p.X - Presenter.Center.X;
        var dy = p.Y - Presenter.Center.Y;
        (_pointerX, _pointerY, _pointerDistance) = PointerField.Normalize(dx, dy, Presenter.Radius);
        if (_press.Phase != PressPhase.Idle)
        {
            (_contactX, _contactY) = ContactFor(dx, dy, Presenter.Radius);
        }

        PushInteraction();
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        Focus(FocusState.Pointer);
        var p = e.GetCurrentPoint(HitArea).Position;
        Presenter.Pointer(CorePointer.Down, p.X, p.Y);

        if (Presenter.HitTestSubsystem(p.X, p.Y) is { } id)
        {
            Presenter.RaiseSubsystemSelected(id);
            e.Handled = true;
            return;
        }

        var dx = p.X - Presenter.Center.X;
        var dy = p.Y - Presenter.Center.Y;
        if (Math.Sqrt(dx * dx + dy * dy) > Presenter.Radius * 1.15) return;

        HitArea.CapturePointer(e.Pointer);
        (_contactX, _contactY) = ContactFor(dx, dy, Presenter.Radius);
        BeginPress();
        e.Handled = true;
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        var p = e.GetCurrentPoint(HitArea).Position;
        Presenter.Pointer(CorePointer.Up, p.X, p.Y);
        if (_press.Phase == PressPhase.Idle) return;
        HitArea.ReleasePointerCapture(e.Pointer);
        Release();
        e.Handled = true;
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Space or VirtualKey.Enter when Presenter.Mode == CoreMode.Exploded:
                Presenter.RaiseSubsystemSelected(_keyboardSubsystem);
                e.Handled = true;
                break;
            case VirtualKey.Space or VirtualKey.Enter:
                if (_press.Phase == PressPhase.Idle)
                {
                    (_contactX, _contactY) = (0, 0);
                    BeginPress();
                }

                e.Handled = true;
                break;
            case VirtualKey.E:
                ExploreRequested?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
                break;
            case VirtualKey.Escape:
                CollapseRequested?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
                break;
            case VirtualKey.Right or VirtualKey.Down when Presenter.Mode == CoreMode.Exploded:
                _keyboardSubsystem = (SubsystemId)(((int)_keyboardSubsystem + 1) % 6);
                Presenter.RaiseSubsystemSelected(_keyboardSubsystem);
                e.Handled = true;
                break;
            case VirtualKey.Left or VirtualKey.Up when Presenter.Mode == CoreMode.Exploded:
                _keyboardSubsystem = (SubsystemId)(((int)_keyboardSubsystem + 5) % 6);
                Presenter.RaiseSubsystemSelected(_keyboardSubsystem);
                e.Handled = true;
                break;
        }
    }

    private void OnKeyUp(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key is VirtualKey.Space or VirtualKey.Enter && _press.Phase != PressPhase.Idle)
        {
            Release();
            e.Handled = true;
        }
    }

    private void BeginPress()
    {
        PressEventRaised?.Invoke(this, _press.Press(Environment.TickCount64));
        _holdTimer ??= CreateHoldTimer();
        _holdTimer.Start();
        PushInteraction();
    }

    private Microsoft.UI.Dispatching.DispatcherQueueTimer CreateHoldTimer()
    {
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(16);
        timer.Tick += (_, _) =>
        {
            if (_press.Update(Environment.TickCount64) is { } charged)
            {
                PressEventRaised?.Invoke(this, charged);
            }

            PushInteraction();
        };
        return timer;
    }

    private void Release()
    {
        _holdTimer?.Stop();
        if (_press.Release(Environment.TickCount64) is { } released)
        {
            PressEventRaised?.Invoke(this, released);
        }

        PushInteraction();
    }

    private void CancelPress()
    {
        _holdTimer?.Stop();
        _press.Cancel();
        PushInteraction();
    }

    private void PushInteraction()
        => Presenter.SetInteraction(new CoreInteraction(_pointerX, _pointerY, _pointerDistance, _press.Force, _gravityX, _gravityY, _contactX, _contactY));

    private static (double X, double Y) ContactFor(double dx, double dy, double radius)
    {
        var r = Math.Max(1, radius);
        return (Math.Clamp(dx / r, -1, 1), Math.Clamp(dy / r, -1, 1));
    }
}

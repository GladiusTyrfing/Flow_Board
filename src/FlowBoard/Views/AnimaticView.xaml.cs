using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using FlowBoard.Converters;
using FlowBoard.ViewModels;

namespace FlowBoard.Views;

public partial class AnimaticView : UserControl
{
    private AnimaticViewModel? _vm;

    public AnimaticView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_vm != null)
            {
                _vm.ShotEntered -= OnShotEntered;
                _vm.PropertyChanged -= OnVmChanged;
            }

            _vm = DataContext as AnimaticViewModel;
            if (_vm == null) return;
            _vm.ShotEntered += OnShotEntered;
            _vm.PropertyChanged += OnVmChanged;
            OnShotEntered(this, "Cut");
        };
        Loaded += (_, _) => Focus();
        PreviewKeyDown += OnKey;
        SizeChanged += (_, _) => UpdateBars();
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (_vm == null) return;
        switch (e.Key)
        {
            case Key.Space:
                _vm.TogglePlayCommand.Execute(null);
                break;
            case Key.Left:
                _vm.PreviousCommand.Execute(null);
                break;
            case Key.Right:
                _vm.NextCommand.Execute(null);
                break;
            case Key.Home:
                _vm.RestartCommand.Execute(null);
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AnimaticViewModel.ShotProgress) or nameof(AnimaticViewModel.TotalProgress)) UpdateBars();
    }

    private void UpdateBars()
    {
        if (_vm == null) return;
        SeekFill.Width = Math.Max(0, Seek.ActualWidth * _vm.TotalProgress);
        ShotFill.Width = 1280 * _vm.ShotProgress;
    }

    private void OnShotEntered(object? sender, string transition)
    {
        if (_vm?.Current == null) return;
        var previous = FrontImage.Source;
        var next = ImageLoader.Load(_vm.Current.ImageFullPath, 1920);
        NoImage.Visibility = next == null ? Visibility.Visible : Visibility.Collapsed;
        FrontImage.BeginAnimation(OpacityProperty, null);
        FrontImage.Clip = null;
        FrontImage.Opacity = 1;
        FrontImage.Source = next;
        BackImage.Source = null;
        if (!Helpers.Behaviors.AnimationsEnabled) return;

        var t = transition.ToLowerInvariant();
        if (t is "fade")
        {
            // Through black.
            FrontImage.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(450)));
        }
        else if (t is "dissolve")
        {
            BackImage.Source = previous;
            FrontImage.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(550)));
        }
        else if (t is "wipe")
        {
            BackImage.Source = previous;
            var clip = new RectangleGeometry(new Rect(0, 0, 0, 720));
            FrontImage.Clip = clip;
            clip.BeginAnimation(RectangleGeometry.RectProperty,
                new RectAnimation(new Rect(0, 0, 0, 720), new Rect(0, 0, 1280, 720), TimeSpan.FromMilliseconds(500)) { EasingFunction = new CubicEase() });
        }
    }

    private void OnSeek(object sender, MouseButtonEventArgs e)
    {
        Seek.CaptureMouse();
        SeekTo(e.GetPosition(Seek).X);
        Seek.MouseLeftButtonUp += Release;

        void Release(object s, MouseButtonEventArgs a)
        {
            Seek.ReleaseMouseCapture();
            Seek.MouseLeftButtonUp -= Release;
        }
    }

    private void OnSeekDrag(object sender, MouseEventArgs e)
    {
        if (Seek.IsMouseCaptured && e.LeftButton == MouseButtonState.Pressed) SeekTo(e.GetPosition(Seek).X);
    }

    private void SeekTo(double x)
    {
        if (_vm == null || Seek.ActualWidth <= 0) return;
        _vm.SeekTotal(x / Seek.ActualWidth);
    }
}

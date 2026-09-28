using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlowBoard.Services;

namespace FlowBoard.ViewModels;

public sealed record AspectChoice(string Name, double Ratio);

/// <summary>Result of the crop dialog. <see cref="Fit"/> = show the whole image (letterboxed) instead of filling the frame.</summary>
public sealed record CropResult(string ImagePath, string OriginalPath, bool Fit, Rect Crop);

/// <summary>Crop an image (free or fixed aspect) or choose to show all of it.</summary>
public sealed partial class ImageCropViewModel : DialogViewModel
{
    private readonly Guid _owner;
    private readonly Action<CropResult> _apply;

    public ImageCropViewModel(string originalRelative, Guid owner, double aspect, Rect? previous, Action<CropResult> apply, string title = "Crop image")
    {
        OriginalPath = originalRelative;
        _owner = owner;
        _apply = apply;
        Title = title;
        _selectedAspect = Aspects.FirstOrDefault(a => Math.Abs(a.Ratio - aspect) < 0.01) ?? Aspects[0];
        if (previous is { Width: > 0 } r) Crop = r;
        try
        {
            var bmp = Converters.ImageLoader.Load(FullPath, 0);
            if (bmp != null) ImageAspect = (double)bmp.PixelWidth / Math.Max(1, bmp.PixelHeight);
        }
        catch
        {
            // Keep the default aspect.
        }

        if (previous is not { Width: > 0 }) FitToAspect();
    }

    public override bool CloseOnBackdropClick => false;
    public string Title { get; }
    public string OriginalPath { get; }
    public string FullPath => AppPaths.ToFull(OriginalPath);
    public double ImageAspect { get; } = 16.0 / 9;

    public static AspectChoice[] Aspects { get; } =
    [
        new("Free", 0), new("16:9", 16.0 / 9), new("4:3", 4.0 / 3), new("1:1", 1), new("2.39:1", 2.39), new("1.85:1", 1.85), new("9:16", 9.0 / 16), new("3:4", 3.0 / 4),
    ];

    [ObservableProperty] private AspectChoice _selectedAspect;

    /// <summary>Crop rectangle in 0..1 image coordinates.</summary>
    [ObservableProperty] private Rect _crop = new(0, 0, 1, 1);

    partial void OnSelectedAspectChanged(AspectChoice value) => FitToAspect();

    [RelayCommand] private void SetAspect(AspectChoice a) => SelectedAspect = a;

    /// <summary>Largest centered rectangle with the chosen aspect.</summary>
    [RelayCommand]
    public void FitToAspect()
    {
        var a = SelectedAspect.Ratio;
        if (a <= 0)
        {
            Crop = new Rect(0, 0, 1, 1);
            return;
        }

        double w = 1, h = 1;
        if (ImageAspect > a) w = a / ImageAspect;
        else h = ImageAspect / a;
        Crop = new Rect((1 - w) / 2, (1 - h) / 2, w, h);
    }

    [RelayCommand]
    private void UseWhole()
    {
        _apply(new CropResult(OriginalPath, OriginalPath, true, new Rect(0, 0, 1, 1)));
        Close();
    }

    [RelayCommand]
    private void Apply()
    {
        try
        {
            var c = Crop;
            if (c.Width > 0.995 && c.Height > 0.995)
            {
                _apply(new CropResult(OriginalPath, OriginalPath, false, new Rect(0, 0, 1, 1)));
                Close();
                return;
            }

            var src = new BitmapImage();
            src.BeginInit();
            src.CacheOption = BitmapCacheOption.OnLoad;
            src.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            src.UriSource = new Uri(FullPath, UriKind.Absolute);
            src.EndInit();
            src.Freeze();
            var rect = new Int32Rect(
                (int)Math.Round(c.X * src.PixelWidth), (int)Math.Round(c.Y * src.PixelHeight),
                Math.Max(1, (int)Math.Round(c.Width * src.PixelWidth)), Math.Max(1, (int)Math.Round(c.Height * src.PixelHeight)));
            rect.Width = Math.Min(rect.Width, src.PixelWidth - rect.X);
            rect.Height = Math.Min(rect.Height, src.PixelHeight - rect.Y);
            var cropped = new CroppedBitmap(src, rect);
            var name = Path.GetFileNameWithoutExtension(OriginalPath) + " (crop).png";
            var rel = MediaStore.SavePng(cropped, _owner, name);
            _apply(new CropResult(rel, OriginalPath, false, c));
            Close();
        }
        catch (Exception ex)
        {
            MainViewModel.Instance.ShowToast($"Couldn't crop the image: {ex.Message}", isError: true);
        }
    }
}

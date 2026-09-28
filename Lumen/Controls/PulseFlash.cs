using Microsoft.UI.Xaml.Media.Animation;

namespace Lumen.Controls;

/// <summary>Brief energy flash shared by pulse receivers: a hairline brightens, then settles.</summary>
internal static class PulseFlash
{
    public static void Play(UIElement target)
    {
        var animation = new DoubleAnimationUsingKeyFrames();
        animation.KeyFrames.Add(new LinearDoubleKeyFrame { KeyTime = TimeSpan.Zero, Value = 0 });
        animation.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = TimeSpan.FromMilliseconds(90), Value = 1, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        animation.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = TimeSpan.FromMilliseconds(700), Value = 0, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } });
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, "Opacity");
        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        storyboard.Begin();
    }
}

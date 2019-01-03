using System;
using System.Drawing;
using System.Reflection;
using System.Collections.Generic;
using PaintDotNet;
using PaintDotNet.Effects;
using PaintDotNet.IndirectUI;
using PaintDotNet.PropertySystem;

namespace BlurFillEffect
{
    public class PluginSupportInfo : IPluginSupportInfo
    {
        public string Author => base.GetType().Assembly.GetCustomAttribute<AssemblyCopyrightAttribute>().Copyright;
        public string Copyright => L10nStrings.EffectDescription;
        public string DisplayName => L10nStrings.EffectName;
        public Version Version => base.GetType().Assembly.GetName().Version;
        public Uri WebsiteUri => new Uri("https://forums.getpaint.net/index.php?showtopic=32224");

        public string plugin_browser_Keywords => L10nStrings.EffectKeywords;
        public string plugin_browser_Description => L10nStrings.EffectDescription;
    }

    [PluginSupportInfo(typeof(PluginSupportInfo))]
    public class BlurFill : PropertyBasedEffect
    {
        private int blurRadius = 10; // [-100,100] Blur Radius
        private int brightness = -100; // [-100,100] Brightness
        private Pair<double, double> position = Pair.Create(0.0, 0.0); // Position Adjust
        private bool keepOriginal = true; // [0,1] Keep original image

        private Surface enlargedSurface;
        private Surface clampedSurface;
        private Surface effectsSurface;
        private Rectangle trimmedBounds = Rectangle.Empty;

        private readonly GaussianBlurEffect blurEffect = new GaussianBlurEffect();
        private readonly BrightnessAndContrastAdjustment bacAdjustment = new BrightnessAndContrastAdjustment();
        private readonly BinaryPixelOp normalOp = LayerBlendModeUtil.CreateCompositionOp(LayerBlendMode.Normal);

        private readonly static Image StaticIcon = new Bitmap(typeof(BlurFill), "BlurFill.png");

        public BlurFill()
            : base(L10nStrings.EffectName, StaticIcon, L10nStrings.EffectMenu, EffectFlags.Configurable)
        {
        }

        public enum PropertyNames
        {
            BlurRadius,
            Brightness,
            Position,
            KeepOriginal
        }

        protected override PropertyCollection OnCreatePropertyCollection()
        {
            List<Property> props = new List<Property>
            {
                new Int32Property(PropertyNames.BlurRadius, 10, 0, 200),
                new Int32Property(PropertyNames.Brightness, 0, -100, 100),
                new DoubleVectorProperty(PropertyNames.Position, Pair.Create(0.0, 0.0), Pair.Create(-1.0, -1.0), Pair.Create(+1.0, +1.0)),
                new BooleanProperty(PropertyNames.KeepOriginal, true)
            };

            return new PropertyCollection(props);
        }

        protected override ControlInfo OnCreateConfigUI(PropertyCollection props)
        {
            ControlInfo configUI = CreateDefaultConfigUI(props);

            configUI.SetPropertyControlValue(PropertyNames.BlurRadius, ControlInfoPropertyNames.DisplayName, L10nStrings.BlurRadius);
            configUI.SetPropertyControlValue(PropertyNames.Brightness, ControlInfoPropertyNames.DisplayName, L10nStrings.Brightness);
            configUI.SetPropertyControlValue(PropertyNames.Position, ControlInfoPropertyNames.DisplayName, L10nStrings.Position);
            configUI.SetPropertyControlValue(PropertyNames.Position, ControlInfoPropertyNames.SliderSmallChangeX, 0.05);
            configUI.SetPropertyControlValue(PropertyNames.Position, ControlInfoPropertyNames.SliderLargeChangeX, 0.25);
            configUI.SetPropertyControlValue(PropertyNames.Position, ControlInfoPropertyNames.UpDownIncrementX, 0.01);
            configUI.SetPropertyControlValue(PropertyNames.Position, ControlInfoPropertyNames.SliderSmallChangeY, 0.05);
            configUI.SetPropertyControlValue(PropertyNames.Position, ControlInfoPropertyNames.SliderLargeChangeY, 0.25);
            configUI.SetPropertyControlValue(PropertyNames.Position, ControlInfoPropertyNames.UpDownIncrementY, 0.01);
            configUI.SetPropertyControlValue(PropertyNames.Position, ControlInfoPropertyNames.DecimalPlaces, 3);
            Rectangle selection3 = EnvironmentParameters.GetSelection(EnvironmentParameters.SourceSurface.Bounds).GetBoundsInt();
            ImageResource imageResource3 = ImageResource.FromImage(EnvironmentParameters.SourceSurface.CreateAliasedBitmap(selection3));
            configUI.SetPropertyControlValue(PropertyNames.Position, ControlInfoPropertyNames.StaticImageUnderlay, imageResource3);
            configUI.SetPropertyControlValue(PropertyNames.KeepOriginal, ControlInfoPropertyNames.DisplayName, string.Empty);
            configUI.SetPropertyControlValue(PropertyNames.KeepOriginal, ControlInfoPropertyNames.Description, L10nStrings.KeepOriginal);

            return configUI;
        }

        protected override void OnSetRenderInfo(PropertyBasedEffectConfigToken newToken, RenderArgs dstArgs, RenderArgs srcArgs)
        {
            blurRadius = newToken.GetProperty<Int32Property>(PropertyNames.BlurRadius).Value;
            brightness = newToken.GetProperty<Int32Property>(PropertyNames.Brightness).Value;
            position = newToken.GetProperty<DoubleVectorProperty>(PropertyNames.Position).Value;
            keepOriginal = newToken.GetProperty<BooleanProperty>(PropertyNames.KeepOriginal).Value;

            Rectangle selection = EnvironmentParameters.GetSelection(srcArgs.Surface.Bounds).GetBoundsInt();

            if (trimmedBounds == Rectangle.Empty)
            {
                trimmedBounds = GetTrimmedBounds(srcArgs.Surface, selection);
            }

            float ratio = (float)selection.Width / selection.Height;
            Size ratioSize = new Size(trimmedBounds.Width, trimmedBounds.Height);
            if (ratioSize.Width < ratioSize.Height * ratio)
            {
                ratioSize.Height = (int)Math.Round(trimmedBounds.Width / ratio);
            }
            else if (ratioSize.Width > ratioSize.Height * ratio)
            {
                ratioSize.Width = (int)Math.Round(trimmedBounds.Height * ratio);
            }

            Rectangle srcRect = new Rectangle
            {
                X = (int)Math.Round(trimmedBounds.X + Math.Abs(ratioSize.Width - trimmedBounds.Width) / 2f + (position.First * Math.Abs(ratioSize.Width - trimmedBounds.Width) / 2f)),
                Y = (int)Math.Round(trimmedBounds.Y + Math.Abs(ratioSize.Height - trimmedBounds.Height) / 2f + (position.Second * Math.Abs(ratioSize.Height - trimmedBounds.Height) / 2f)),
                Width = ratioSize.Width,
                Height = ratioSize.Height
            };

            if (enlargedSurface == null)
            {
                enlargedSurface = new Surface(selection.Size);
            }

            using (Surface ratioSurface = new Surface(ratioSize))
            {
                ratioSurface.CopySurface(srcArgs.Surface, Point.Empty, srcRect);
                enlargedSurface.FitSurface(ResamplingAlgorithm.Bicubic, ratioSurface);
            }

            if (selection.Size != srcArgs.Surface.Size)
            {
                if (clampedSurface == null)
                {
                    clampedSurface = new Surface(srcArgs.Surface.Size);
                }

                for (int y = Math.Max(0, selection.Top - 200); y < Math.Min(clampedSurface.Height, selection.Bottom + 200); y++)
                {
                    if (IsCancelRequested) return;
                    for (int x = Math.Max(0, selection.Left - 200); x < Math.Min(clampedSurface.Width, selection.Right + 200); x++)
                    {
                        clampedSurface[x, y] = enlargedSurface.GetBilinearSampleClamped(x - selection.Left, y - selection.Top);
                    }
                }
            }
            else
            {
                clampedSurface = enlargedSurface;
            }

            if (effectsSurface == null)
            {
                effectsSurface = new Surface(srcArgs.Surface.Size);
            }

            // Setup for calling the Gaussian Blur effect
            PropertyCollection blurProps = blurEffect.CreatePropertyCollection();
            PropertyBasedEffectConfigToken BlurParameters = new PropertyBasedEffectConfigToken(blurProps);
            BlurParameters.SetPropertyValue(GaussianBlurEffect.PropertyNames.Radius, blurRadius);
            blurEffect.SetRenderInfo(BlurParameters, new RenderArgs(effectsSurface), new RenderArgs(clampedSurface));

            // Setup for calling the Brightness and Contrast Adjustment function
            PropertyCollection bacProps = bacAdjustment.CreatePropertyCollection();
            PropertyBasedEffectConfigToken bacParameters = new PropertyBasedEffectConfigToken(bacProps);
            bacParameters.SetPropertyValue(BrightnessAndContrastAdjustment.PropertyNames.Brightness, brightness);
            bacParameters.SetPropertyValue(BrightnessAndContrastAdjustment.PropertyNames.Contrast, 0);
            bacAdjustment.SetRenderInfo(bacParameters, new RenderArgs(effectsSurface), new RenderArgs(effectsSurface));

            base.OnSetRenderInfo(newToken, dstArgs, srcArgs);
        }

        protected override void OnRender(Rectangle[] renderRects, int startIndex, int length)
        {
            if (length == 0) return;
            for (int i = startIndex; i < startIndex + length; ++i)
            {
                Render(DstArgs.Surface, SrcArgs.Surface, renderRects[i]);
            }
        }

        private static Rectangle GetTrimmedBounds(Surface srcSurface, Rectangle srcBounds)
        {
            int xMin = int.MaxValue,
                xMax = int.MinValue,
                yMin = int.MaxValue,
                yMax = int.MinValue;

            bool foundPixel = false;

            // Find xMin
            for (int x = srcBounds.Left; x < srcBounds.Right; x++)
            {
                bool stop = false;
                for (int y = srcBounds.Top; y < srcBounds.Bottom; y++)
                {
                    if (srcSurface[x, y].A != 0)
                    {
                        xMin = x;
                        stop = true;
                        foundPixel = true;
                        break;
                    }
                }
                if (stop)
                {
                    break;
                }
            }

            // Image is empty...
            if (!foundPixel)
            {
                return srcBounds;
            }

            // Find yMin
            for (int y = srcBounds.Top; y < srcBounds.Bottom; y++)
            {
                bool stop = false;
                for (int x = xMin; x < srcBounds.Right; x++)
                {
                    if (srcSurface[x, y].A != 0)
                    {
                        yMin = y;
                        stop = true;
                        break;
                    }
                }
                if (stop)
                {
                    break;
                }
            }

            // Find xMax
            for (int x = srcBounds.Right - 1; x >= xMin; x--)
            {
                bool stop = false;
                for (int y = yMin; y < srcBounds.Bottom; y++)
                {
                    if (srcSurface[x, y].A != 0)
                    {
                        xMax = x;
                        stop = true;
                        break;
                    }
                }
                if (stop)
                {
                    break;
                }
            }

            // Find yMax
            for (int y = srcBounds.Bottom - 1; y >= yMin; y--)
            {
                bool stop = false;
                for (int x = xMin; x <= xMax; x++)
                {
                    if (srcSurface[x, y].A != 0)
                    {
                        yMax = y;
                        stop = true;
                        break;
                    }
                }
                if (stop)
                {
                    break;
                }
            }

            return Rectangle.FromLTRB(xMin, yMin, xMax + 1, yMax + 1);
        }

        private void Render(Surface dst, Surface src, Rectangle rect)
        {
            // Call the Gaussian Blur function
            blurEffect.Render(new Rectangle[1] { rect }, 0, 1);

            // Call the Brightness and Contrast Adjustment function
            bacAdjustment.Render(new Rectangle[1] { rect }, 0, 1);

            if (keepOriginal)
            {
                normalOp.Apply(effectsSurface, rect.Location, src, rect.Location, rect.Size);
            }

            dst.CopySurface(effectsSurface, rect.Location, rect);
        }

        protected override void OnDispose(bool disposing)
        {
            if (disposing)
            {
                enlargedSurface?.Dispose();
                clampedSurface?.Dispose();
                effectsSurface?.Dispose();
                blurEffect?.Dispose();
                bacAdjustment?.Dispose();
            }

            base.OnDispose(disposing);
        }
    }
}

﻿using System;
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
        private bool keepOriginal = true;

        private Surface enlargedSurface;
        private Surface clampedSurface;
        private Surface effectsSurface;
        private Rectangle trimmedBounds = Rectangle.Empty;

        private readonly GaussianBlurEffect blurEffect = new GaussianBlurEffect();
        private readonly BrightnessAndContrastAdjustment bacAdjustment = new BrightnessAndContrastAdjustment();
        private readonly BinaryPixelOp normalOp = LayerBlendModeUtil.CreateCompositionOp(LayerBlendMode.Normal);

        private readonly static Image StaticIcon = new Bitmap(typeof(BlurFill), "BlurFill.png");

        public BlurFill()
            : base(L10nStrings.EffectName, StaticIcon, L10nStrings.EffectMenu, new EffectOptions { Flags = EffectFlags.Configurable })
        {
        }

        private enum PropertyNames
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
            Rectangle selection = this.EnvironmentParameters.SelectionBounds;
            ImageResource imageResource = ImageResource.FromImage(this.EnvironmentParameters.SourceSurface.CreateAliasedBitmap(selection));
            configUI.SetPropertyControlValue(PropertyNames.Position, ControlInfoPropertyNames.StaticImageUnderlay, imageResource);
            configUI.SetPropertyControlValue(PropertyNames.KeepOriginal, ControlInfoPropertyNames.DisplayName, string.Empty);
            configUI.SetPropertyControlValue(PropertyNames.KeepOriginal, ControlInfoPropertyNames.Description, L10nStrings.KeepOriginal);

            return configUI;
        }

        protected override void OnSetRenderInfo(PropertyBasedEffectConfigToken token, RenderArgs dstArgs, RenderArgs srcArgs)
        {
            int blurRadius = token.GetProperty<Int32Property>(PropertyNames.BlurRadius).Value;
            int brightness = token.GetProperty<Int32Property>(PropertyNames.Brightness).Value;
            Pair<double, double> position = token.GetProperty<DoubleVectorProperty>(PropertyNames.Position).Value;
            this.keepOriginal = token.GetProperty<BooleanProperty>(PropertyNames.KeepOriginal).Value;

            Rectangle selection = this.EnvironmentParameters.SelectionBounds;

            if (this.trimmedBounds.IsEmpty)
            {
                this.trimmedBounds = GetTrimmedBounds(srcArgs.Surface, selection);
            }

            float ratio = (float)selection.Width / selection.Height;
            Size ratioSize = new Size(this.trimmedBounds.Width, this.trimmedBounds.Height);
            if (ratioSize.Width < ratioSize.Height * ratio)
            {
                ratioSize.Height = (int)Math.Round(this.trimmedBounds.Width / ratio);
            }
            else if (ratioSize.Width > ratioSize.Height * ratio)
            {
                ratioSize.Width = (int)Math.Round(this.trimmedBounds.Height * ratio);
            }

            PointF offsetForCenter = new PointF(Math.Abs(ratioSize.Width - this.trimmedBounds.Width) / 2f, Math.Abs(ratioSize.Height - this.trimmedBounds.Height) / 2f);
            Rectangle srcRect = new Rectangle
            {
                X = (int)Math.Round(this.trimmedBounds.X + offsetForCenter.X + (position.First * offsetForCenter.X)),
                Y = (int)Math.Round(this.trimmedBounds.Y + offsetForCenter.Y + (position.Second * offsetForCenter.Y)),
                Width = ratioSize.Width,
                Height = ratioSize.Height
            };

            if (this.enlargedSurface == null)
            {
                this.enlargedSurface = new Surface(selection.Size);
            }

            using (Surface ratioSurface = new Surface(ratioSize))
            {
                ratioSurface.CopySurface(srcArgs.Surface, Point.Empty, srcRect);
                this.enlargedSurface.FitSurface(ResamplingAlgorithm.Bicubic, ratioSurface);
            }

            if (selection.Size != srcArgs.Surface.Size)
            {
                if (this.clampedSurface == null)
                {
                    this.clampedSurface = new Surface(srcArgs.Surface.Size);
                }

                for (int y = Math.Max(0, selection.Top - 200); y < Math.Min(this.clampedSurface.Height, selection.Bottom + 200); y++)
                {
                    if (this.IsCancelRequested) return;
                    for (int x = Math.Max(0, selection.Left - 200); x < Math.Min(this.clampedSurface.Width, selection.Right + 200); x++)
                    {
                        this.clampedSurface[x, y] = this.enlargedSurface.GetBilinearSampleClamped(x - selection.Left, y - selection.Top);
                    }
                }
            }
            else
            {
                this.clampedSurface = this.enlargedSurface;
            }

            if (this.effectsSurface == null)
            {
                this.effectsSurface = new Surface(srcArgs.Surface.Size);
            }

            // Setup for calling the Gaussian Blur effect
            PropertyCollection blurProps = this.blurEffect.CreatePropertyCollection();
            PropertyBasedEffectConfigToken BlurParameters = new PropertyBasedEffectConfigToken(blurProps);
            BlurParameters.SetPropertyValue(GaussianBlurEffect.PropertyNames.Radius, blurRadius);
            this.blurEffect.SetRenderInfo(BlurParameters, new RenderArgs(this.effectsSurface), new RenderArgs(this.clampedSurface));

            // Setup for calling the Brightness and Contrast Adjustment function
            PropertyCollection bacProps = this.bacAdjustment.CreatePropertyCollection();
            PropertyBasedEffectConfigToken bacParameters = new PropertyBasedEffectConfigToken(bacProps);
            bacParameters.SetPropertyValue(BrightnessAndContrastAdjustment.PropertyNames.Brightness, brightness);
            bacParameters.SetPropertyValue(BrightnessAndContrastAdjustment.PropertyNames.Contrast, 0);
            this.bacAdjustment.SetRenderInfo(bacParameters, new RenderArgs(this.effectsSurface), new RenderArgs(this.effectsSurface));

            base.OnSetRenderInfo(token, dstArgs, srcArgs);
        }

        protected override void OnRender(Rectangle[] renderRects, int startIndex, int length)
        {
            if (length == 0) return;

            // Call the Gaussian Blur function
            this.blurEffect.Render(renderRects, startIndex, length);

            // Call the Brightness and Contrast Adjustment function
            this.bacAdjustment.Render(renderRects, startIndex, length);

            if (this.keepOriginal)
            {
                this.normalOp.Apply(this.effectsSurface, this.SrcArgs.Surface, renderRects, startIndex, length);
            }

            this.DstArgs.Surface.CopySurface(this.effectsSurface, renderRects, startIndex, length);
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

        protected override void OnDispose(bool disposing)
        {
            if (disposing)
            {
                this.enlargedSurface?.Dispose();
                this.clampedSurface?.Dispose();
                this.effectsSurface?.Dispose();
                this.blurEffect?.Dispose();
                this.bacAdjustment?.Dispose();
            }

            base.OnDispose(disposing);
        }
    }
}

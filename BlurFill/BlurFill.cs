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
        public string Author => ((AssemblyCopyrightAttribute)base.GetType().Assembly.GetCustomAttributes(typeof(AssemblyCopyrightAttribute), false)[0]).Copyright;
        public string Copyright => ((AssemblyDescriptionAttribute)base.GetType().Assembly.GetCustomAttributes(typeof(AssemblyDescriptionAttribute), false)[0]).Description;
        public string DisplayName => ((AssemblyProductAttribute)base.GetType().Assembly.GetCustomAttributes(typeof(AssemblyProductAttribute), false)[0]).Product;
        public Version Version => base.GetType().Assembly.GetName().Version;
        public Uri WebsiteUri => new Uri("https://forums.getpaint.net/index.php?showtopic=32224");
    }

    [PluginSupportInfo(typeof(PluginSupportInfo), DisplayName = "Blur Fill")]

    public class BlurFill : PropertyBasedEffect
    {
        int Amount1 = 10; // [-100,100] Blur Radius
        int Amount2 = -100; // [-100,100] Brightness
        Pair<double, double> Amount3 = Pair.Create(0.0, 0.0); // Position Adjust
        bool Amount4 = true; // [0,1] Keep original image

        Surface enlargedSurface;
        Surface alignedSurface;
        Surface bluredSurface;
        Surface brightSurface;
        Surface TrimmedSurface;

        readonly GaussianBlurEffect blurEffect = new GaussianBlurEffect();
        readonly BrightnessAndContrastAdjustment bacAdjustment = new BrightnessAndContrastAdjustment();
        readonly BinaryPixelOp normalOp = LayerBlendModeUtil.CreateCompositionOp(LayerBlendMode.Normal);


        const string StaticName = "Blur Fill";
        readonly static Image StaticIcon = new Bitmap(typeof(BlurFill), "BlurFill.png");
        const string SubmenuName = "Fill";

        public BlurFill()
            : base(StaticName, StaticIcon, SubmenuName, EffectFlags.Configurable)
        {
        }

        public enum PropertyNames
        {
            Amount1,
            Amount2,
            Amount3,
            Amount4
        }

        protected override PropertyCollection OnCreatePropertyCollection()
        {
            List<Property> props = new List<Property>();

            props.Add(new Int32Property(PropertyNames.Amount1, 10, 0, 200));
            props.Add(new Int32Property(PropertyNames.Amount2, 0, -100, 100));
            props.Add(new DoubleVectorProperty(PropertyNames.Amount3, Pair.Create(0.0, 0.0), Pair.Create(-1.0, -1.0), Pair.Create(+1.0, +1.0)));
            props.Add(new BooleanProperty(PropertyNames.Amount4, true));

            return new PropertyCollection(props);
        }

        protected override ControlInfo OnCreateConfigUI(PropertyCollection props)
        {
            ControlInfo configUI = CreateDefaultConfigUI(props);

            configUI.SetPropertyControlValue(PropertyNames.Amount1, ControlInfoPropertyNames.DisplayName, "Blur Radius");
            configUI.SetPropertyControlValue(PropertyNames.Amount2, ControlInfoPropertyNames.DisplayName, "Brightness");
            configUI.SetPropertyControlValue(PropertyNames.Amount3, ControlInfoPropertyNames.DisplayName, "Position Adjust");
            configUI.SetPropertyControlValue(PropertyNames.Amount3, ControlInfoPropertyNames.SliderSmallChangeX, 0.05);
            configUI.SetPropertyControlValue(PropertyNames.Amount3, ControlInfoPropertyNames.SliderLargeChangeX, 0.25);
            configUI.SetPropertyControlValue(PropertyNames.Amount3, ControlInfoPropertyNames.UpDownIncrementX, 0.01);
            configUI.SetPropertyControlValue(PropertyNames.Amount3, ControlInfoPropertyNames.SliderSmallChangeY, 0.05);
            configUI.SetPropertyControlValue(PropertyNames.Amount3, ControlInfoPropertyNames.SliderLargeChangeY, 0.25);
            configUI.SetPropertyControlValue(PropertyNames.Amount3, ControlInfoPropertyNames.UpDownIncrementY, 0.01);
            configUI.SetPropertyControlValue(PropertyNames.Amount3, ControlInfoPropertyNames.DecimalPlaces, 3);
            Rectangle selection3 = EnvironmentParameters.GetSelection(EnvironmentParameters.SourceSurface.Bounds).GetBoundsInt();
            ImageResource imageResource3 = ImageResource.FromImage(EnvironmentParameters.SourceSurface.CreateAliasedBitmap(selection3));
            configUI.SetPropertyControlValue(PropertyNames.Amount3, ControlInfoPropertyNames.StaticImageUnderlay, imageResource3);
            configUI.SetPropertyControlValue(PropertyNames.Amount4, ControlInfoPropertyNames.DisplayName, string.Empty);
            configUI.SetPropertyControlValue(PropertyNames.Amount4, ControlInfoPropertyNames.Description, "Keep original image");

            return configUI;
        }

        protected override void OnSetRenderInfo(PropertyBasedEffectConfigToken newToken, RenderArgs dstArgs, RenderArgs srcArgs)
        {
            Amount1 = newToken.GetProperty<Int32Property>(PropertyNames.Amount1).Value;
            Amount2 = newToken.GetProperty<Int32Property>(PropertyNames.Amount2).Value;
            Amount3 = newToken.GetProperty<DoubleVectorProperty>(PropertyNames.Amount3).Value;
            Amount4 = newToken.GetProperty<BooleanProperty>(PropertyNames.Amount4).Value;


            Rectangle selection = EnvironmentParameters.GetSelection(srcArgs.Surface.Bounds).GetBoundsInt();

            if (TrimmedSurface == null)
            {
                using (Surface selSurface = new Surface(selection.Size))
                {
                    selSurface.CopySurface(srcArgs.Surface, selection);
                    TrimmedSurface = TrimBitmap(selSurface);
                }

                if (TrimmedSurface == null)
                    TrimmedSurface = new Surface(selection.Size);
            }

            float ratio = (float)selection.Width / selection.Height;
            Size ratioSize = new Size(TrimmedSurface.Width, TrimmedSurface.Height);
            if (ratioSize.Width < ratioSize.Height * ratio)
                ratioSize.Height = (int)Math.Round(TrimmedSurface.Width / ratio);
            else if (ratioSize.Width > ratioSize.Height * ratio)
                ratioSize.Width = (int)Math.Round(TrimmedSurface.Height * ratio);

            Point offset = new Point
            {
                X = (int)Math.Round((ratioSize.Width - TrimmedSurface.Width) / 2f + (Amount3.First * (ratioSize.Width - TrimmedSurface.Width) / 2f)),
                Y = (int)Math.Round((ratioSize.Height - TrimmedSurface.Height) / 2f + (Amount3.Second * (ratioSize.Height - TrimmedSurface.Height) / 2f))
            };


            if (enlargedSurface == null)
                enlargedSurface = new Surface(selection.Size);

            using (Surface ratioSurface = new Surface(ratioSize))
            {
                ratioSurface.CopySurface(TrimmedSurface, offset);
                enlargedSurface.FitSurface(ResamplingAlgorithm.Bicubic, ratioSurface);
            }

            if (alignedSurface == null)
                alignedSurface = new Surface(srcArgs.Surface.Size);

            if (selection.Size != srcArgs.Surface.Size)
            {
                for (int y = Math.Max(0, selection.Top - 200); y < Math.Min(alignedSurface.Height, selection.Bottom + 200); y++)
                {
                    if (IsCancelRequested) return;
                    for (int x = Math.Max(0, selection.Left - 200); x < Math.Min(alignedSurface.Width, selection.Right + 200); x++)
                    {
                        alignedSurface[x, y] = enlargedSurface.GetBilinearSampleClamped(x - selection.Left, y - selection.Top);
                    }
                }
            }
            else
            {
                alignedSurface = enlargedSurface;
            }

            if (bluredSurface == null)
                bluredSurface = new Surface(srcArgs.Surface.Size);
            if (brightSurface == null)
                brightSurface = new Surface(srcArgs.Surface.Size);

            // Setup for calling the Gaussian Blur effect
            PropertyCollection blurProps = blurEffect.CreatePropertyCollection();
            PropertyBasedEffectConfigToken BlurParameters = new PropertyBasedEffectConfigToken(blurProps);
            BlurParameters.SetPropertyValue(GaussianBlurEffect.PropertyNames.Radius, Amount1);
            blurEffect.SetRenderInfo(BlurParameters, new RenderArgs(bluredSurface), new RenderArgs(alignedSurface));

            // Setup for calling the Brightness and Contrast Adjustment function
            PropertyCollection bacProps = bacAdjustment.CreatePropertyCollection();
            PropertyBasedEffectConfigToken bacParameters = new PropertyBasedEffectConfigToken(bacProps);
            bacParameters.SetPropertyValue(BrightnessAndContrastAdjustment.PropertyNames.Brightness, Amount2);
            bacParameters.SetPropertyValue(BrightnessAndContrastAdjustment.PropertyNames.Contrast, 0);
            bacAdjustment.SetRenderInfo(bacParameters, new RenderArgs(brightSurface), new RenderArgs(bluredSurface));


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

        static Surface TrimBitmap(Surface source)
        {
            Rectangle srcRect = Rectangle.Empty;

            int xMin = int.MaxValue,
                xMax = int.MinValue,
                yMin = int.MaxValue,
                yMax = int.MinValue;

            bool foundPixel = false;

            // Find xMin
            for (int x = 0; x < source.Width; x++)
            {
                bool stop = false;
                for (int y = 0; y < source.Height; y++)
                {
                    if (source[x, y].A != 0)
                    {
                        xMin = x;
                        stop = true;
                        foundPixel = true;
                        break;
                    }
                }
                if (stop)
                    break;
            }

            // Image is empty...
            if (!foundPixel)
                return null;

            // Find yMin
            for (int y = 0; y < source.Height; y++)
            {
                bool stop = false;
                for (int x = xMin; x < source.Width; x++)
                {
                    if (source[x, y].A != 0)
                    {
                        yMin = y;
                        stop = true;
                        break;
                    }
                }
                if (stop)
                    break;
            }

            // Find xMax
            for (int x = source.Width - 1; x >= xMin; x--)
            {
                bool stop = false;
                for (int y = yMin; y < source.Height; y++)
                {
                    if (source[x, y].A != 0)
                    {
                        xMax = x;
                        stop = true;
                        break;
                    }
                }
                if (stop)
                    break;
            }

            // Find yMax
            for (int y = source.Height - 1; y >= yMin; y--)
            {
                bool stop = false;
                for (int x = xMin; x <= xMax; x++)
                {
                    if (source[x, y].A != 0)
                    {
                        yMax = y;
                        stop = true;
                        break;
                    }
                }
                if (stop)
                    break;
            }

            srcRect = Rectangle.FromLTRB(xMin, yMin, xMax + 1, yMax + 1);

            Surface trimmed = new Surface(srcRect.Size);
            trimmed.CopySurface(source, Point.Empty, srcRect);

            return trimmed;
        }

        void Render(Surface dst, Surface src, Rectangle rect)
        {
            // Call the Gaussian Blur function
            blurEffect.Render(new Rectangle[1] { rect }, 0, 1);

            // Call the Brightness and Contrast Adjustment function
            bacAdjustment.Render(new Rectangle[1] { rect }, 0, 1);

            if (Amount4)
                normalOp.Apply(brightSurface, rect.Location, src, rect.Location, rect.Size);

            dst.CopySurface(brightSurface, rect.Location, rect);
        }

    }
}
using System;
using System.Drawing;
using System.Reflection;
using System.Collections.Generic;
using PaintDotNet;
using PaintDotNet.Effects;
using PaintDotNet.IndirectUI;
using PaintDotNet.PropertySystem;
using System.Drawing.Imaging;

namespace BlurFillEffect
{
    public class PluginSupportInfo : IPluginSupportInfo
    {
        public string Author
        {
            get
            {
                return ((AssemblyCopyrightAttribute)base.GetType().Assembly.GetCustomAttributes(typeof(AssemblyCopyrightAttribute), false)[0]).Copyright;
            }
        }
        public string Copyright
        {
            get
            {
                return ((AssemblyDescriptionAttribute)base.GetType().Assembly.GetCustomAttributes(typeof(AssemblyDescriptionAttribute), false)[0]).Description;
            }
        }

        public string DisplayName
        {
            get
            {
                return ((AssemblyProductAttribute)base.GetType().Assembly.GetCustomAttributes(typeof(AssemblyProductAttribute), false)[0]).Product;
            }
        }

        public Version Version
        {
            get
            {
                return base.GetType().Assembly.GetName().Version;
            }
        }

        public Uri WebsiteUri
        {
            get
            {
                return new Uri("http://www.getpaint.net/redirect/plugins.html");
            }
        }
    }

    [PluginSupportInfo(typeof(PluginSupportInfo), DisplayName = "Blur Fill")]

    public class BlurFill : PropertyBasedEffect
    {
        public static string StaticName
        {
            get
            {
                return "Blur Fill";
            }
        }

        public static Image StaticIcon
        {
            get
            {
                //return new Bitmap(typeof(BlurFill), "BlurFill.png");
                return null;
            }
        }

        public static string SubmenuName
        {
            get
            {
                return "Fill";
            }
        }

        public BlurFill()
            : base(StaticName, StaticIcon, SubmenuName, EffectFlags.Configurable)
        {
        }

        public enum PropertyNames
        {
            Amount1,
            Amount2,
            Amount3
        }

        protected override PropertyCollection OnCreatePropertyCollection()
        {
            List<Property> props = new List<Property>();

            props.Add(new Int32Property(PropertyNames.Amount1, 10, 0, 200));
            props.Add(new Int32Property(PropertyNames.Amount2, 0, -100, 100));
            props.Add(new DoubleVectorProperty(PropertyNames.Amount3, Pair.Create(0.0, 0.0), Pair.Create(-1.0, -1.0), Pair.Create(+1.0, +1.0)));

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
            Rectangle selection3 = EnvironmentParameters.GetSelection(EnvironmentParameters.SourceSurface.Bounds).GetBoundsInt();
            ImageResource imageResource3 = ImageResource.FromImage(EnvironmentParameters.SourceSurface.CreateAliasedBitmap(selection3));
            configUI.SetPropertyControlValue(PropertyNames.Amount3, ControlInfoPropertyNames.StaticImageUnderlay, imageResource3);

            return configUI;
        }

        protected override void OnSetRenderInfo(PropertyBasedEffectConfigToken newToken, RenderArgs dstArgs, RenderArgs srcArgs)
        {
            Amount1 = newToken.GetProperty<Int32Property>(PropertyNames.Amount1).Value;
            Amount2 = newToken.GetProperty<Int32Property>(PropertyNames.Amount2).Value;
            Amount3 = newToken.GetProperty<DoubleVectorProperty>(PropertyNames.Amount3).Value;

            base.OnSetRenderInfo(newToken, dstArgs, srcArgs);


            Rectangle selection = EnvironmentParameters.GetSelection(srcArgs.Surface.Bounds).GetBoundsInt();

            double ratio = (float)selection.Height / (float)selection.Width;

            Bitmap srcBitmap = srcArgs.Surface.CreateAliasedBitmap();

            Bitmap croppedBitmap = TrimBitmap(srcBitmap, ratio, Amount3.First, Amount3.Second);

            Surface croppedSurface = Surface.CopyFromBitmap(croppedBitmap);

            enlargedSurface = new Surface(srcArgs.Surface.Size);
            bluredSurface = new Surface(srcArgs.Surface.Size);
            lightSurface = new Surface(srcArgs.Surface.Size);

            enlargedSurface.FitSurface(ResamplingAlgorithm.Bicubic, croppedSurface);
        }

        protected override unsafe void OnRender(Rectangle[] rois, int startIndex, int length)
        {
            if (length == 0) return;
            for (int i = startIndex; i < startIndex + length; ++i)
            {
                Render(DstArgs.Surface, SrcArgs.Surface, rois[i]);
            }
        }

        #region User Entered Code
        #region UICode
        int Amount1 = 10; // [-100,100] Blur Radius
        int Amount2 = -100; // [-100,100] Brightness
        Pair<double, double> Amount3 = Pair.Create(0.0, 0.0); // Position Adjust
        #endregion

        private BinaryPixelOp normalOp = LayerBlendModeUtil.CreateCompositionOp(LayerBlendMode.Normal);

        Surface enlargedSurface;
        Surface bluredSurface;
        Surface lightSurface;

        static Bitmap TrimBitmap(Bitmap source, double ratio, double offsetX, double offsetY)
        {
            Rectangle srcRect = default(Rectangle);
            System.Drawing.Imaging.BitmapData data = null;
            try
            {
                data = source.LockBits(new Rectangle(0, 0, source.Width, source.Height), System.Drawing.Imaging.ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                byte[] buffer = new byte[data.Height * data.Stride];
                System.Runtime.InteropServices.Marshal.Copy(data.Scan0, buffer, 0, buffer.Length);

                int xMin = int.MaxValue,
                    xMax = int.MinValue,
                    yMin = int.MaxValue,
                    yMax = int.MinValue;

                bool foundPixel = false;

                // Find xMin
                for (int x = 0; x < data.Width; x++)
                {
                    bool stop = false;
                    for (int y = 0; y < data.Height; y++)
                    {
                        byte alpha = buffer[y * data.Stride + 4 * x + 3];
                        if (alpha != 0)
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
                for (int y = 0; y < data.Height; y++)
                {
                    bool stop = false;
                    for (int x = xMin; x < data.Width; x++)
                    {
                        byte alpha = buffer[y * data.Stride + 4 * x + 3];
                        if (alpha != 0)
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
                for (int x = data.Width - 1; x >= xMin; x--)
                {
                    bool stop = false;
                    for (int y = yMin; y < data.Height; y++)
                    {
                        byte alpha = buffer[y * data.Stride + 4 * x + 3];
                        if (alpha != 0)
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
                for (int y = data.Height - 1; y >= yMin; y--)
                {
                    bool stop = false;
                    for (int x = xMin; x <= xMax; x++)
                    {
                        byte alpha = buffer[y * data.Stride + 4 * x + 3];
                        if (alpha != 0)
                        {
                            yMax = y;
                            stop = true;
                            break;
                        }
                    }
                    if (stop)
                        break;
                }

                srcRect = Rectangle.FromLTRB(xMin, yMin, xMax, yMax);
            }
            finally
            {
                if (data != null)
                    source.UnlockBits(data);
            }


            int bitmapWidth;
            int bitmapHeight;

            if ((float)srcRect.Height <= (float)srcRect.Width * ratio)
            {
                bitmapWidth = (int)(srcRect.Height / ratio);
                bitmapHeight = srcRect.Height;
            }
            else
            {
                bitmapWidth = srcRect.Width;
                bitmapHeight = (int)(srcRect.Width * ratio);
            }

            int bitmapOffsetX = (int)((bitmapWidth - srcRect.Width) / 2 - (offsetX * (bitmapWidth - srcRect.Width) / 2));
            int bitmapOffsetY = (int)((bitmapHeight - srcRect.Height) / 2 - (offsetY * (bitmapHeight - srcRect.Height) / 2));

            Bitmap dest = new Bitmap(bitmapWidth, bitmapHeight);
            Rectangle destRect = new Rectangle(bitmapOffsetX, bitmapOffsetY, srcRect.Width, srcRect.Height);
            using (Graphics graphics = Graphics.FromImage(dest))
            {
                graphics.DrawImage(source, destRect, srcRect, GraphicsUnit.Pixel);
            }
            return dest;
        }

        void Render(Surface dst, Surface src, Rectangle rect)
        {
            // Setup for calling the Gaussian Blur effect
            GaussianBlurEffect blurEffect = new GaussianBlurEffect();
            PropertyCollection blurProps = blurEffect.CreatePropertyCollection();
            PropertyBasedEffectConfigToken BlurParameters = new PropertyBasedEffectConfigToken(blurProps);
            BlurParameters.SetPropertyValue(GaussianBlurEffect.PropertyNames.Radius, Amount1);
            blurEffect.SetRenderInfo(BlurParameters, new RenderArgs(bluredSurface), new RenderArgs(enlargedSurface));
            // Call the Gaussian Blur function
            blurEffect.Render(new Rectangle[1] { rect }, 0, 1);

            // Setup for calling the Brightness and Contrast Adjustment function
            BrightnessAndContrastAdjustment bacAdjustment = new BrightnessAndContrastAdjustment();
            PropertyCollection bacProps = bacAdjustment.CreatePropertyCollection();
            PropertyBasedEffectConfigToken bacParameters = new PropertyBasedEffectConfigToken(bacProps);
            bacParameters.SetPropertyValue(BrightnessAndContrastAdjustment.PropertyNames.Brightness, Amount2);
            bacParameters.SetPropertyValue(BrightnessAndContrastAdjustment.PropertyNames.Contrast, 0);
            bacAdjustment.SetRenderInfo(bacParameters, new RenderArgs(lightSurface), new RenderArgs(bluredSurface));
            // Call the Brightness and Contrast Adjustment function
            bacAdjustment.Render(new Rectangle[1] { rect }, 0, 1);

            for (int y = rect.Top; y < rect.Bottom; y++)
            {
                if (IsCancelRequested) return;
                for (int x = rect.Left; x < rect.Right; x++)
                {
                    dst[x, y] = normalOp.Apply(lightSurface.GetBilinearSample(x, y), src[x, y]); 
                }
            }
        }
        #endregion
    }
}
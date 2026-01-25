// Requires the following NuGet packages for .net Core
// * System.Drawing.

using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Diagnostics;
using System.Text;

namespace BollardBlogger {
    static class ImageFile {
        // Property IDs listed at: https://learn.microsoft.com/en-us/windows/win32/gdiplus/-gdiplus-constant-image-property-tag-constants
        const Int32 c_propId_Orientation = 0x0112;
        const Int32 c_propId_ThumbnailFirst = 0x5012;
        const Int32 c_propId_ThumbnailNot = 0x501A; // ColorTransferFunction is the one property in the range that's not associated with the thumbnail
        const Int32 c_propId_ThumbnailLast = 0x503B;
        const EncoderValue c_encoderValueZero = (EncoderValue)0; // Actually the same as ColorTypeCMYK but that value is not used.

        public static void ResizeAndRightImage(Stream src, Stream dst, int width, int height) {
            // Load the image to change
            using (Image? image = Image.FromStream(src)) {
                if (image is null)
                    throw new Exception("Failed to load image from stream.");
                int interimWidth = width;
                int interimHeight = height;
                RotateFlipType rft = RotateFlipType.RotateNoneFlipNone;

                var imgprops = image.PropertyItems;

                // Check the orientation and determine whether image must be rotated
                {
                    // Image.GetPropertyItem throws an exception if the image is not Exif
                    // So we use the property collection instead
                    var orientation =
                    (from ip in imgprops
                     where ip.Id == c_propId_Orientation
                     select ip.Value).FirstOrDefault();

                    if (orientation != null) {
                        switch (orientation[0]) {
                            // case 1: // Vertical
                            //  do nothing;
                            //  brea;
                            case 2: // FlipHorizontal
                                rft = RotateFlipType.RotateNoneFlipX;
                                break;
                            case 3: // Rotated 180
                                rft = RotateFlipType.Rotate180FlipNone;
                                break;
                            case 4: // FlipVertical
                                rft = RotateFlipType.Rotate180FlipX;
                                break;
                            case 5:
                                rft = RotateFlipType.Rotate90FlipX;
                                interimWidth = height;
                                interimHeight = width;
                                break;
                            case 6: // Rotated 270
                                rft = RotateFlipType.Rotate90FlipNone;
                                interimWidth = height;
                                interimHeight = width;
                                break;
                            case 8: // Rotated 90
                                rft = RotateFlipType.Rotate270FlipNone;
                                interimWidth = height;
                                interimHeight = width;
                                break;
                        }
                    }
                }

                using (var resizedImage = new Bitmap(interimWidth, interimHeight)) {
                    resizedImage.SetResolution(72, 72);

                    // Copy metadata and fix rotation.
                    foreach (var prop in imgprops) {
                        if (prop.Id == c_propId_Orientation && prop.Value is not null) {
                            prop.Value[0] = 1;  // Set it back to vertical.
                        }
                        else if (IsThumbnailProperty(prop.Id)) {
                            continue;   // Suppress copying the thumbnail
                        }
                        resizedImage.SetPropertyItem(prop);
                    }

                    using (var graphic = Graphics.FromImage(resizedImage)) {
                        graphic.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                        graphic.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                        graphic.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                        graphic.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
                        graphic.DrawImage(image, 0, 0, interimWidth, interimHeight);
                    }

                    if (rft != RotateFlipType.RotateNoneFlipNone)
                        resizedImage.RotateFlip(rft);
                    resizedImage.Save(dst, image.RawFormat);
                }
            }
        }

        public static void RightImage(Stream src, Stream dst) {
            // Load the image to rotate
            using (var image = Image.FromStream(src)) {
                // Get the existing orientation
                var piOrientation = image.GetPropertyItem(c_propId_Orientation);
                if (piOrientation is null || piOrientation.Value is null)
                    throw new Exception("Failed to retrieve orientaton property.");
                Debug.Assert(piOrientation.Id == c_propId_Orientation);
                Debug.Assert(piOrientation.Type == 3);
                Debug.Assert(piOrientation.Len == 2);

                // Set the encoder value according to existing orientation
                EncoderValue ev;
                switch (piOrientation.Value[0]) {
                    case 2: // FlipHorizontal
                        ev = EncoderValue.TransformFlipHorizontal;
                        break;

                    case 3: // Rotated 180
                        ev = EncoderValue.TransformRotate180;
                        break;

                    case 4: // FlipVertical
                        ev = EncoderValue.TransformFlipVertical;
                        break;

                    case 6: // Rotated 270
                        ev = EncoderValue.TransformRotate90;
                        break;

                    case 8: // Rotated 90
                        ev = EncoderValue.TransformRotate270;
                        break;

                    case 1: // Normal
                    default:
                        src.CopyTo(dst);
                        return;
                }

                // Change the orientation to 1 (normal) as we will rotate during the export
                piOrientation.Value[0] = 1;
                image.SetPropertyItem(piOrientation);

                // Prep the encoder parameters
                var encParams = new EncoderParameters(1);
                encParams.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Transformation, (long)ev);

                // Write the image with a rotation transformation
                image.Save(dst, JpegCodecInfo, encParams);
            }
        }

        static ImageCodecInfo? s_jpegCodecInfo;

        static ImageCodecInfo JpegCodecInfo {
            get {
                if (s_jpegCodecInfo == null) {
                    foreach (var encoder in ImageCodecInfo.GetImageEncoders()) {
                        if (encoder!.MimeType!.Equals("image/jpeg", StringComparison.OrdinalIgnoreCase)) {
                            s_jpegCodecInfo = encoder;
                            break;
                        }
                    }
                    if (s_jpegCodecInfo == null) {
                        throw new ApplicationException("Unable to locate GDI+ JPEG Encoder");
                    }
                }
                return s_jpegCodecInfo;
            }
        }

        static HashSet<Int32> s_includedPropIds = new HashSet<Int32>();
        static HashSet<Int32> s_suppressPropIds = new HashSet<Int32>();

        static bool IsThumbnailProperty(Int32 propId) {
            return propId >= c_propId_ThumbnailFirst && propId <= c_propId_ThumbnailLast && propId != c_propId_ThumbnailNot;
        }

    }
}

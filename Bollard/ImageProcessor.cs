using System;
using System.Text;
using System.Diagnostics;
using System.IO;
using System.Diagnostics.CodeAnalysis;

namespace BollardBlogger {
    /// <summary>
    /// Class to prepare images for the bollard blog
    /// Facilitates matching duplicates, transferring metadata, and resizing
    /// This is a console class and writes error messages directly to the
    /// console before returning.
    /// </summary>
    class ImageProcessor {
        const int c_maxBaseName = 24;

        // Property keys retrieved from https://msdn.microsoft.com/en-us/library/windows/desktop/dd561977(v=vs.85).aspx
        static Interop.PropertyKey s_pkTitle = new Interop.PropertyKey("F29F85E0-4FF9-1068-AB91-08002B27B3D9", 2); // System.Title
        static Interop.PropertyKey s_pkComment = new Interop.PropertyKey("F29F85E0-4FF9-1068-AB91-08002B27B3D9", 6); // System.Comment
        static Interop.PropertyKey s_pkKeywords = new Interop.PropertyKey("F29F85E0-4FF9-1068-AB91-08002B27B3D9", 5); // System.Keywords
        static Interop.PropertyKey s_pkWidth = new Interop.PropertyKey("6444048F-4C8B-11D1-8B70-080036B11A03", 3);
        static Interop.PropertyKey s_pkHeight = new Interop.PropertyKey("6444048F-4C8B-11D1-8B70-080036B11A03", 4);
        static Interop.PropertyKey s_pkDateTaken = new Interop.PropertyKey("14B81DA1-0135-4D31-96D9-6CBFC9671A99", 36867); // System.Photo.DateTaken
        static Interop.PropertyKey s_pkLatitude = new Interop.PropertyKey("8727CFFF-4868-4EC6-AD5B-81B98521D1AB", 100); // System.GPS.Latitude
        static Interop.PropertyKey s_pkLatitudeRef = new Interop.PropertyKey("029C0252-5B86-46C7-ACA0-2769FFC8E3D4", 100); // System.GPS.LatitudeRef
        static Interop.PropertyKey s_pkLongitude = new Interop.PropertyKey("C4C4DBB2-B593-466B-BBDA-D03D27D5E43A", 100); // System.GPS.Longitude
        static Interop.PropertyKey s_pkLongitudeRef = new Interop.PropertyKey("33DCF22B-28D5-464C-8035-1EE9EFD25278", 100); // System.GPS.LongitudeRef
        static Interop.PropertyKey s_pkOrientation = new Interop.PropertyKey("14B81DA1-0135-4D31-96D9-6CBFC9671A99", 274);

        List<Size> m_generateSizes = new List<Size>();
        string m_imageDirectory = string.Empty;

        /// <summary>
        /// A list of <see cref="Size"/> indicating the maximum sizes of images to be generated.
        /// </summary>
        /// <remarks>
        /// <para>One resized image will be generated for each of the sizes in this list. The resized
        /// images retain their original aspect ratio and are sized to fit within the specified
        /// dimensions. Typically the sizes in this list are square. So, for example, if a 4:3
        /// aspect ratio image provided, a size of 1000x1000 will result in a 1000x750 image.
        /// </para>
        /// <para>If an image is smaller than a specified size then the original will simply
        /// be copied over. For example, if an 800x600 image is provided then a size of 1000x1000
        /// will still result in an 800x600 output.
        /// </para>
        /// </remarks>
        public IList<Size> GenerateSizes => m_generateSizes;

        public string ImageDirectory { get { return m_imageDirectory; } set { m_imageDirectory = value; } }

        /// <summary>
        /// Load the photo and retrieve metadata
        /// </summary>
        /// <param name="localFilePath">The file path on the local machine or network.</param>
        /// <returns>True if the file was found, and is JPEG.</returns>
        /// <remarks>Upon a false return, error details are in the <see cref="ErrorMessage"/> property. </remarks>
        public ImageInfo ProcessImage(string localFilePath) {
            {
                string extension = Path.GetExtension(localFilePath);
                if (!extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)) {
                    throw new ApplicationException($"Image to post must be a JPEG file. '{Path.GetFileName(localFilePath)}' is not.");
                }
            }

            var ii = new ImageInfo();

            int orientation = 1;
            try {
                using (var propStore = WinShell.PropertyStore.Open(localFilePath)) {
                    ii.Title = propStore.GetValue(s_pkTitle) as string ?? string.Empty;
                    ii.Comment = propStore.GetValue(s_pkComment) as string ?? string.Empty;
                    ii.DateTaken = propStore.GetValue(s_pkDateTaken) as DateTime? ?? DateTime.MinValue;
                    ii.OriginalSize = new Size((int)(UInt32)propStore.GetValue(s_pkWidth)!,
                        (int)(UInt32)propStore.GetValue(s_pkHeight)!);
                    ii.Latitude = GetLatOrLong(propStore, true);
                    ii.Longitude = GetLatOrLong(propStore, false);
                    var tags = propStore.GetValue(s_pkKeywords) as string[];
                    if (tags is not null)
                        ii.Tags = tags;
                    orientation = (int)(ushort)(propStore.GetValue(s_pkOrientation) ?? ((ushort)1));
                }
            }
            catch (Exception err) {
                throw new ApplicationException($"Image '{Path.GetFileName(localFilePath)}': Failed to read metadata from photo: {err.Message}", err);
            }

            if (string.IsNullOrEmpty(ii.Title)) {
                throw new ApplicationException($"Image '{Path.GetFileName(localFilePath)}' does not have a title.");
            }
            if (ii.DateTaken == DateTime.MinValue) {
                throw new ApplicationException($"Image '{Path.GetFileName(localFilePath)}' does not have a date taken.");
            }

            // =========================
            // Note that when orientation is 90 degrees or 270 degrees, the Windows Property System
            // returns the dimensions of the rotated image. So width and height DO NOT have to be swapped.

            // Base filename
            ii.BaseName = string.Concat(
                ii.DateTaken.ToString("yyyy-MM-dd"),
                "_",
                NameFromTitle(ii.Title, c_maxBaseName));

            // Generate images of each size
            int n = m_generateSizes.Count;

			ii.Sizes = new Size[n];
            ii.Filenames = new string[n];
            for (int i = 0; i < n; i++) {
                var size = LimitSize(ii.OriginalSize, m_generateSizes[i]);
                var filename = $"{ii.BaseName}_{size.Width}x{size.Height}.jpg";
                bool generated = GenerateImage(localFilePath, ii.OriginalSize, orientation, Path.Combine(m_imageDirectory, filename), size);
				ii.Sizes[i] = size;
				ii.Filenames[i] = filename;
                if (generated) ii.Generated = true;
            }

            return ii;
        }

        private Size LimitSize(Size original, Size limit) {
            Size result;

            // First, assume that width will be the governing factor
            if (original.Width <= limit.Width) {
                result.Width = original.Width;
                result.Height = original.Height;
            }
            else {
                result.Width = limit.Width;
                result.Height = (limit.Width * original.Height) / original.Width; // Scale height to match width
            }

            // If using width-dominance is too tall then use height dominance
            if (result.Height > limit.Height) {
                Debug.Assert(original.Height > limit.Height);
                result.Height = limit.Height;
                result.Width = (limit.Height * original.Width) / original.Height; // Scale width to match height
            }
            return result;
        }

        private static bool GenerateImage(string srcPath, Size srcSize, int srcOrientation, string dstPath, Size dstSize) {
            if (File.Exists(dstPath))
                return false;

            // If it needs to be resized, do so
            if (!srcSize.Equals(dstSize)) {
                using (var srcImage = File.OpenRead(srcPath)) {
                    using (var dstImage = File.OpenWrite(dstPath)) {
                        ImageFile.ResizeAndRightImage(srcImage, dstImage, dstSize.Width, dstSize.Height);
                    }
                }
            }
            // If it needs to be righted, do so
            else if (srcOrientation != 1) {
                using (var srcImage = File.OpenRead(srcPath)) {
                    using (var dstImage = File.OpenWrite(dstPath)) {
                        ImageFile.RightImage(srcImage, dstImage);
                    }
                }
            }
            // Otherwise copy it
            else {
                File.Copy(srcPath, dstPath);
            }
            return true;
        }

        private static string NameFromTitle(string title, int maxLength) {
            var sb = new StringBuilder();
            int len = title.Length;
            for (int i = 0; i < len;) {
                // Get the next word
                char c = title[i];
                while (!char.IsLetter(c) && !char.IsLetter(c)) {
                    ++i;
                    if (i >= len)
                        break;
                    c = title[i];
                }
                if (i >= len)
                    break;
                var sbWord = new StringBuilder();
                while (char.IsLetter(c) || char.IsDigit(c)) {
                    sbWord.Append(c);
                    ++i;
                    if (i >= len)
                        break;
                    c = title[i];
                }
                var word = sbWord.ToString();
                if (string.Equals(word, "the", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (sb.Length > 0 && sb.Length + word.Length > maxLength)
                    break;
                sb.Append(word);
            }
            return sb.ToString();
        }

        private static double GetLatOrLong(WinShell.PropertyStore store, bool getLatitude) {
            // Get the property keys
            Interop.PropertyKey pkValue;
            Interop.PropertyKey pkDirection;
            if (getLatitude) {
                pkValue = s_pkLatitude;
                pkDirection = s_pkLatitudeRef;
            }
            else {
                pkValue = s_pkLongitude;
                pkDirection = s_pkLongitudeRef;
            }

            // Retrieve the values
            double[]? angle = (double[]?)store.GetValue(pkValue);
            string? direction = (string?)store.GetValue(pkDirection);
            if (angle == null || angle.Length == 0 || direction == null)
                return 0.0;

            // Convert to double
            double value = angle[0];
            if (angle.Length > 1)
                value += angle[1] / 60.0;
            if (angle.Length > 2)
                value += angle[2] / 3600.0;

            if (direction.Equals("W", StringComparison.OrdinalIgnoreCase) || direction.Equals("S", StringComparison.OrdinalIgnoreCase)) {
                value = -value;
            }

            return value;
        }
    }

    class ImageInfo {
        private static string[] s_emptyStringArray = new string[0];
        private static Size[] s_emptySizeArray = new Size[0];

        public ImageInfo() {
            Title = String.Empty;
            Comment = String.Empty;
            Latitude = Double.NaN;
            Longitude = Double.NaN;
            Tags = s_emptyStringArray;
            BaseName = String.Empty;
            Sizes = s_emptySizeArray;
            Filenames = s_emptyStringArray;
        }

        // From the original image
        public string Title;
        public string Comment;
        public DateTime DateTaken;
        public double Latitude; // NaN if not present
        public double Longitude; // NaN if not present
        public string[] Tags;
        public Size OriginalSize;

        // Generated data
        public string BaseName;
        public Size[] Sizes;
        public string[] Filenames;
        public bool Generated; // False if all files already existed
    }

    struct Size {
        public Size() { }

		public Size(int squareSize) {
			Width = squareSize;
			Height = squareSize;
		}

		public Size(int width, int height) {
            Width = width;
            Height = height;
        }

        public int Width;
		public int Height;

		public Size Multiply(int factor) {
            return new Size(Width * factor, Height * factor);
        }

        public Size Divide(int factor) {
            return new Size(Width / factor, Height / factor);
        }
        
        public override bool Equals([NotNullWhen(true)] object? obj) {
            if (obj is null) return false;
            return Equals((Size)obj);
        }

		public override int GetHashCode() {
			return Height.GetHashCode() ^ Width.GetHashCode();
		}

		public bool Equals(Size other) {
            return Height == other.Height && Width == other.Width;
        }

        public override string ToString() {
            return $"{Width}x{Height}";
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Dynamic;
using System.Diagnostics.CodeAnalysis;
using Bredd.Razor;

namespace BollardBlogger {
	public class ImageCollection : PostCollection {

		// These sizes are in actual pixels which, due to high-resolution "retina" displays are twice the size of web pixels
		const int c_thumbMaxWidth = 800;
		const int c_thumbMaxHeight = 600;
		const int c_postMaxWidth = 1600;
		const int c_postMaxHeight = 1200;
		const int c_largeMaxSize = 3200;
		const string c_imageFolder = "images";

		string m_imageSrcFolder;

		public ImageCollection(Site site, string name, string layout, string imageSrcFolder)
			: base(site, name, layout) {

			m_imageSrcFolder = imageSrcFolder;
		}

		[UnconditionalSuppressMessage("Trimming", "IL2026:Using dynamic types might cause types or members to be removed by trimmer.", Justification = "<Pending>")]
		public override void Prep() {
			string imageDstPath = Site.PhysicalPathFromSitePath(Name, c_imageFolder);
			if (!Directory.Exists(imageDstPath)) Directory.CreateDirectory(imageDstPath);

			// Convert all of the images but don't render the pages
			var ih = new ImageProcessor();
			ih.ImageDirectory = imageDstPath;
			ih.GenerateSizes.Add(new Size(c_thumbMaxWidth, c_thumbMaxHeight));
			ih.GenerateSizes.Add(new Size(c_postMaxWidth, c_postMaxHeight));
			ih.GenerateSizes.Add(new Size(c_largeMaxSize));

			int nGenerated = 0;
			foreach (var filename in Directory.EnumerateFiles(m_imageSrcFolder, "*.jpg")) {
				var ii = ih.ProcessImage(filename);
				if (ii.Generated) {
					Console.WriteLine($"Generated images for: {ii.Title}");
					++nGenerated;
				}

				dynamic x = new ExpandoObject();
				x.ThumbImage = $"{Path}/{c_imageFolder}/{ii.Filenames[0]}";
				x.PostImage = $"{Path}/{c_imageFolder}/{ii.Filenames[1]}";
                x.LargeImage = $"{Path}/{c_imageFolder}/{ii.Filenames[2]}";
				var size = ii.Sizes[0].Divide(2); // From actual pixels to web pixels
                x.ThumbHeight = size.Height;
				x.ThumbWidth = size.Width;
                size = ii.Sizes[1].Divide(2); // From actual pixels to web pixels
				x.PostHeight = size.Height;
				x.PostWidth = size.Width;
                size = ii.Sizes[2].Divide(2); // From actual pixels to web pixels
				x.LargeHeight = size.Height;
				x.LargeWidth = size.Width;
				x.Title = ii.Title;
				x.Comment = ii.Comment;
				x.Date = ii.DateTaken;
				x.Latitude = ii.Latitude;
				x.Longitude = ii.Longitude;
				x.Name = ii.BaseName;
				x.Collection = Name; // Name of the collection
				x.Path = $"{Path}/{ii.BaseName}.html";
				x.Url = $"{Site.Url}/{Path}/{ii.BaseName}.html";
				Pages.Add(x);
			}
			Pages.Sort((a,b) => a.Date.CompareTo(b.Date));
			int index = 0;
			foreach (var page in Pages) {
				page.Index = index++;
			}

			Console.WriteLine($"{Pages.Count} Posts in the '{Name}' collection.");
			Console.WriteLine($"{nGenerated} Posts for which images were generated.");
		}

		public override void Render() {
			// Get the template
			var template = Site.RazorBit.GetTemplateInstance(Layout);
			dynamic x = new ExpandoObject();
			x.Pages = Pages;
			template.SetContext(collection: x);
			
			// Render the template once for each image
			foreach(dynamic page in Pages) {
				var pageContent = template.Run(Site, page).ToString();
				var path = Site.PhysicalPathFromSitePath(page.Path);
				path = path.Replace("/", "\\");
                using (var writer = new StreamWriter(File.Create(path), Site.Utf8NoBom)) {
					writer.Write(pageContent);
				}
			}
			Console.WriteLine($"Rendered Collection '{Name}': {Pages.Count} entries.");
		}
	}
}

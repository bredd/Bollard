using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Bredd.Razor;
using System.Diagnostics;
using System.Dynamic;
using System.Xml.Linq;
using System.IO;
using Markdig;
using System.Text.Json.Nodes;
using System.Data;

namespace BollardBlogger {
	public class Site {
		public static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

		static readonly MarkdownPipeline s_mdPipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

        Dictionary<string, PostCollection> m_collections = new Dictionary<string, PostCollection>();
		Defaults[] m_allDefaults;

		// Construct a site from a configuration file
		public Site(string defaultFolder, JsonObject config) {
			LocalSrcFolder = ((string?)config["srcFolder"]) ?? defaultFolder;
			LocalDstFolder = ((string?)config["output"]) ?? Path.Combine(LocalSrcFolder, "_site");
			Url = ((string?)config["siteRoot"]) ?? string.Empty;

			// Read the defaults
			JsonArray? jsonDefaults = config["defaults"] as JsonArray;
			if (jsonDefaults is not null) {
				var listDefaults = new List<Defaults>();
				foreach (JsonNode item in jsonDefaults!) {
					JsonObject? objDefault = item as JsonObject;
					if (objDefault is not null) {
						listDefaults.Add(new Defaults(objDefault));
					}
				}
                m_allDefaults = listDefaults.ToArray();
            }
			else {
				m_allDefaults = [];
			}

            // TODO: Replace this with collections
            string? imagePath = (string?)config["images"];
            if (imagePath is not null) {
                var imageCollection = new ImageCollection(this, "bollards", Path.Combine(LocalSrcFolder, @"_layouts\bollard.cshtml"), Path.Combine(LocalSrcFolder, imagePath));
                Collections.Add(imageCollection.Name, imageCollection);
            }

			_srcFolders.Add("/");
            RazorBit = new RazorBit(LocalSrcFolder);
		}

		// Construct a single-file site
		public Site(string srcFilePath) {
			LocalDstFolder = LocalSrcFolder = Path.GetDirectoryName(srcFilePath)!;
			Url = string.Empty;
            m_allDefaults = [];
            _srcFiles.Add(Path.GetFileName(srcFilePath));
            RazorBit = new RazorBit(LocalSrcFolder);
        }

        public string LocalSrcFolder { get; private set; }
		public string LocalDstFolder { get; private set; }
		public string Url { get; private set; }
		public RazorBit RazorBit { get; private set; }

		private List<string> _srcFiles = new List<string>();
		private List<string> _srcFolders = new List<string>();
		private HashSet<string> _dstDirectoryExists = new HashSet<string>();

		public string SourcePathFromSitePath(string siteRelativePath) {
            if (siteRelativePath[0] == '/') {
                siteRelativePath = siteRelativePath.Substring(1);
            }
            var path = Path.Combine(LocalSrcFolder, siteRelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!PathStartsWith(path, LocalSrcFolder))
                throw new ApplicationException($"Attempt to access parent of source directory: {siteRelativePath}");
            return path;
        }

        public string PhysicalPathFromSitePath(string siteRelativePath) {
			if (siteRelativePath[0] == '/') {
				siteRelativePath = siteRelativePath.Substring(1);
			}
			var path = Path.Combine(LocalDstFolder, siteRelativePath.Replace('/', Path.DirectorySeparatorChar));
			if (!PathStartsWith(path, LocalDstFolder))
				throw new ApplicationException($"Attempt to access parent of source directory: {siteRelativePath}");
			return path;
		}

		public string PhysicalPathFromSitePath(params string[] parts) {
			string path = LocalDstFolder;
			foreach(var part in parts) {
				path = Path.Combine(path, part);
			}
			if (!PathStartsWith(path, LocalDstFolder))
				throw new ApplicationException($"Attempt to access parent of source directory: {string.Join("/", parts)}");
			return path;
		}

		public string SitePathFromPhysical(string physicalPath) {
			if (!PathStartsWith(physicalPath, LocalDstFolder))
				throw new ApplicationException($"PhysicalPath is not within the site directory: {physicalPath}");
			string sitePath = physicalPath.Substring(LocalSrcFolder.Length).Replace('\\', '/');
			if (sitePath[0] != '/')
				sitePath = "/" + sitePath;
			return sitePath;
		}

		public string SitePathFromPhysical(params string[] parts) {
			string path = LocalDstFolder;
			foreach (var part in parts) {
				path = Path.Combine(path, part);
			}
			if (!PathStartsWith(path, LocalDstFolder))
				throw new ApplicationException($"PhysicalPath is not within the site directory: {path}");
			string sitePath = path.Substring(LocalDstFolder.Length).Replace('\\', '/');
			if (sitePath[0] != '/')
				sitePath = "/" + sitePath;
			return sitePath;
		}

		public static string SitePathCombine(string path, string subpath) {
            var result = new List<string>();
			SitePathAddPart(result, path);
			SitePathAddPart(result, subpath);
			return "/" + string.Join('/', result);
		}

		private static void SitePathAddPart(List<string> path, string subpath) {
			subpath.TrimEnd('/', '\\');
            foreach (string part in subpath.Split('/', '\\')) {
                if (part.Length == 0) {
                    path.Clear();
                }
                else if (part == ".") {
                    continue;
                }
                else if (part == "..") {
					if (path.Count > 0) path.RemoveAt(path.Count - 1);
                }
				else {
					path.Add(part);
				}
            }
        }

        public IDictionary<string, PostCollection> Collections { get {  return m_collections; } }

		public void Prep() {
			foreach (var collection in Collections) {
				collection.Value.Prep();
			}
		}

		public void Render() {
			foreach(var collection in Collections) {
				collection.Value.Render();
			}
			foreach(var folder in _srcFolders) { 
				RenderFolder(folder);
			}
			foreach (var file in _srcFiles) {
				RenderFile(file);
			}
        }

        private void RenderFolder(string path) {
			// Prep the paths
			Debug.Assert(path.StartsWith("/"));
			var tail = path.Substring(1).Replace('/', '\\');
            var srcFolder = Path.Combine(LocalSrcFolder, tail);
			var sd = new DirectoryInfo(srcFolder);

			// Process the files
			foreach (var fi in sd.EnumerateFiles()) {
				if (fi.Name[0] == '_') {
					// Skip, do nothing
					continue;
				}
				RenderFile(SitePathCombine(path, fi.Name));
			}

			// Process subfolders
			foreach(var di in sd.GetDirectories()) {
				if (di.Name[0] != '_') {
					RenderFolder(SitePathCombine(path, di.Name));
				}
			}
		}

		private void RenderFile(string sitePath) {
			var extension = Path.GetExtension(sitePath);
			if (extension.Equals(".cshtml", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".razor", StringComparison.OrdinalIgnoreCase)) {
                // Render a razor file
                RenderRazor(sitePath);
            }
            else if (extension.Equals(".md", StringComparison.OrdinalIgnoreCase)) {
                RenderMarkdown(sitePath);
            }
            else {
                // Copy the file if it is newer
				var sfi = new FileInfo(SourcePathFromSitePath(sitePath));
				var dfi = new FileInfo(PhysicalPathFromSitePath(sitePath));
                if (!dfi.Exists || dfi.LastWriteTimeUtc < sfi.LastWriteTimeUtc) {
					CreateDirIfNeeded(dfi.DirectoryName!);
                    Console.WriteLine($"Copy: {sitePath}");
                    sfi.CopyTo(dfi.FullName, true);
                }
            }
        }

        private void RenderRazor(string sitePath) {
            dynamic pageInfo = RbHelps.Clone(GetDefaults(sitePath).Values);
            AddNameInfo(sitePath, pageInfo);

            var template = RazorBit.GetTemplateInstance(sitePath);

			var pageContent = template.Run(site: RbHelps.ToExpando(this), page: pageInfo);

			var destPath = PhysicalPathFromSitePath(pageInfo.Path);
			CreateDirIfNeeded(Path.GetDirectoryName(destPath));
            using (var writer = new StreamWriter(File.Create(destPath), Utf8NoBom)) {
                writer.Write(pageContent);
            }
            Console.WriteLine($"Render: {pageInfo.Path}");
		}

		private void RenderMarkdown(string sitePath) {
			dynamic pageInfo = RbHelps.Clone(GetDefaults(sitePath).Values);
			AddNameInfo(sitePath, pageInfo);
			string md;
			using (var reader = new StreamReader(SourcePathFromSitePath(sitePath))) {
				md = reader.ReadToEnd();
			}
			var html = Markdown.ToHtml(md, s_mdPipeline);

			// TODO: Find out the right way to do this
			string? layout = ((IDictionary<string, object?>)pageInfo).ContainsKey("Layout") ? pageInfo.Layout : null;
			if (layout is not null) {
				var template = RazorBit.GetTemplateInstance(layout);
				pageInfo.Layout = null;
				html = template.Run(site: RbHelps.ToExpando(this), page: pageInfo, bodyContent: new StringBuilder(html)).ToString();
			}

            var destPath = PhysicalPathFromSitePath(pageInfo.Path);
			CreateDirIfNeeded(Path.GetDirectoryName(destPath));
            using (var writer = new StreamWriter(File.Create(destPath), Utf8NoBom)) {
                writer.Write(html);
            }
            Console.WriteLine($"Render: {sitePath}");
        }

        private void AddNameInfo(string srcFilename, dynamic pageInfo) {
            int iName = srcFilename.LastIndexOf('/');
            iName = (iName >= 0) ? iName + 1 : 0;
            int iExt = srcFilename.LastIndexOf('.');
            if (iExt < iName)
                throw new ArgumentException("Source filename must have an extension");

            pageInfo.Name = srcFilename.Substring(iName, iExt - iName) + ".html";
			var path = srcFilename.Substring(0, iExt) + ".html";
			pageInfo.Path = path;
            pageInfo.Url = Url + path;
        }

        private static bool PathStartsWith(string path, string basePath) {
			// Normalize slashes
			path = path.Replace('\\', '/');
			basePath = basePath.Replace('\\', '/');

			// Base includes trailing slash
			if (!basePath.EndsWith('/')) basePath += "/";
			return path.StartsWith(basePath);
		}

		private Defaults GetDefaults(string sitePath, string? collectionName = null) {
			var ext = Path.GetExtension(sitePath);
			Defaults? bestMatch = Defaults.DefaultDefaults;
			int bestRank = -1;

			foreach(var def in m_allDefaults) {
				var rank = 0;
				if (def.Ext is not null && string.Equals(ext, def.Ext, StringComparison.OrdinalIgnoreCase))
					rank += 250;
				if (def.Collection is not null && string.Equals(collectionName, def.Collection, StringComparison.Ordinal))
					rank += 250;
				if (def.Path is not null && sitePath.StartsWith(def.Path, StringComparison.OrdinalIgnoreCase))
					rank += def.Path.Length;

				if (rank > bestRank) {
					bestRank = rank;
					bestMatch = def;
				}
			}
            return bestMatch;
        }

		private void CreateDirIfNeeded(string directoryPath) {
			if (_dstDirectoryExists.Add(directoryPath)) {
				Directory.CreateDirectory(directoryPath);
			}
		}

        class Defaults {
			static public readonly Defaults DefaultDefaults = new Defaults(null, null, null, new ExpandoObject());

            public Defaults(string? path, string? ext, string? collection, dynamic values) {
                Path = path;
                Ext = ext;
                Collection = collection;
                Values = values;
            }
            
			public Defaults(JsonObject json) {
				JsonObject? scope = json["scope"] as JsonObject;
                if (scope != null)
                {
					Path = (string?)scope["path"];
					Ext = (string?)scope["ext"];
					Collection = (string?)scope["collection"];                    
                }
				ExpandoObject values = new ExpandoObject();
				var dict = (IDictionary<string, object?>)values;
				JsonObject? jsonValues = json["values"] as JsonObject;
				if (jsonValues != null) {
					foreach(KeyValuePair<string, JsonNode> pair in jsonValues!) {
						var val = (string?)pair.Value;
						if (val is not null)
							dict[pair.Key] = val;
					}
				}
				Values = values;
			}
			public string? Path;
			public string? Ext;
			public string? Collection;
			public dynamic Values;
		}
	}
}

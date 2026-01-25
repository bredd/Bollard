using Microsoft.AspNetCore.Razor.Language;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Dynamic;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Web;

// Requires references to the following NuGet Packages:
// Microsoft.AspNetCore.Mvc.Razor.Runtime
// Microsoft.AspNetCore.Razor.Language
// Microsoft.CodeAnalysis.CSharp

// Inspired and informed by:
// https://www.daveaglick.com/posts/the-bleeding-edge-of-razor
// https://www.codeproject.com/Articles/5260233/Building-String-Razor-Template-Engine-with-Bare-Ha
// https://github.com/Merlin04/RazorEngineCore
// https://learn.microsoft.com/en-us/aspnet/core/mvc/views/razor

/* Unlike other Razor Compiler support tools, this CodeBit doesn't simply wrap the Razor
 * complier. Rather, this uses extension functions and classes to make RazorProjectEngine
 * and related classes easier to call and use. This means you can always bypass RazorBit
 * and call functions directly when needed or augment the extensions with features you need.
 */

// This implementation supports the following directives and possibly more
// @inherits
// @implements
// @functions

/* Development Nodes
 * 
 * In regular ASP.Net Microsoft.AspNetCore.Html.IHtmlContent is returned from HtmlHelper.raw.
 * we may use Microsoft.AspNetCore.Html, or we may make a compatible class.
 */

namespace Bredd.Razor {

    public class RazorBit {
        RazorProjectEngine m_engine;
        Dictionary<string, Type> m_templateCache = new Dictionary<string, Type>();

        /// <summary>
        /// Create a project that does not have any designed source directory.
        /// </summary>
        /// <remarks>
        /// A project of this type only compiles Razor from strings. It does not read from files.
        /// </remarks>
        public RazorBit() {
            m_engine = new RbConfiguration().CreateEngine();
        }

        /// <summary>
        /// Create a project from a designated source directory.
        /// </summary>
        /// <param name="sourceDirectory">The name of the source directory containing the razor source files including Layout views.</param>
        public RazorBit(string sourceDirectory) {
            var configuration = new RbConfiguration() { SourceDirectory = sourceDirectory };
            m_engine = configuration.CreateEngine();
        }

        /// <summary>
        /// Create a project from an RbConfiguration
        /// </summary>
        /// <param name="configuration">An RbConfiguration.</param>
        public RazorBit(RbConfiguration configuration) {
            m_engine = configuration.CreateEngine();
        }

        /// <summary>
        /// Get the RazorProjectEngine for this project.
        /// </summary>
        public RazorProjectEngine Engine { get { return m_engine; } }

        /// <summary>
        /// Get a <see cref="Type"/> representing a Razor template from a filename or path.
        /// </summary>
        /// <param name="filename">The name filename, or path.</param>
        /// <returns>An <see cref="Type"/> corresponding to the specified name.</returns>
        /// <exception cref="FileNotFoundException">The specified Razor template was not found.</exception>
        /// <exception cref="RbCompileException">Failed to compile the Razor template.</exception>
        /// <remarks>
        /// <para>GetTemplateType first checks the template cache for a pre-compiled template with
        /// the specified name. If not cached, it attempts to find a file by that name or path in the
        /// <see cref="RazorProjectFileSystem"/> associated with the project and engine. If the
        /// file is not found, or if no source directory was specified, then it throws a
        /// <see cref="FileNotFoundException"/>.
        /// </para>
        /// <para>For projects that aren't sourced from files, you can preload the cache by
        /// calling <see cref="CompileTemplate(string, string)"/> or by supplying a custom
        /// implementation of <see cref="RazorProjectFileSystem"/> in the
        /// <see cref="RbConfiguration"/> when creating <see cref="RazorBit"/>.
        /// </para>
        /// </remarks>
        public Type GetTemplateType(string name) {
            if (m_templateCache.TryGetValue(name, out var template))
                return template;
            template = Engine.ProcessFile(name).Compile();
            m_templateCache[name] = template;
            return template;
        }

        /// <summary>
        /// Instantiate a template with the specified name and base type.
        /// </summary>
        /// <typeparam name="TBaseType">The base type from which the template should inherit.</typeparam>
        /// <param name="name">The name of the template.</param>
        /// <returns>The instantiated template.</returns>
        /// <remarks>
        /// <para>The name should be a valid file system name or path within the
        /// <see cref="RbConfiguration.SourceDirectory"/> or <see cref="RbConfiguration.FileSystem"/>
        /// specified at creation of the <see cref="RazorBit"/>.
        /// </para>
        /// <para>The created template instance will be configured with the <see cref="Site"/>,
        /// <see cref="Page"/>, and <see cref="Model"/> values if they are non-null.
        /// </para>
        /// <para>The GetTemplateType delegate of the created template will be set to this
        /// RazorBit's <see cref="GetTemplateType"/> instance.</para>
        /// </remarks>
        public TBaseType GetTemplateInstance<TBaseType>(string name) where TBaseType : class {
            var instance = RbHelps.InstantiateTemplate<TBaseType>(GetTemplateType(name));
            if (instance is RbTemplate rbTemplate) {
                Func<string, Type> gtt = this.GetTemplateType;
                rbTemplate.SetContext(getTemplateType: gtt);
            }
            return instance;
        }

        /// <summary>
        /// Instantiate a template with the specified name and base type.
        /// </summary>
        /// <param name="name">The name of the template.</param>
        /// <returns>The instantiated template.</returns>
        /// <remarks>
        /// <para>The name should be a valid file system name or path within the
        /// <see cref="RbConfiguration.SourceDirectory"/> or <see cref="RbConfiguration.FileSystem"/>
        /// specified at creation of the <see cref="RazorBit"/>.
        /// </para>
        /// <para>The created template instance will be configured with the <see cref="Site"/>,
        /// <see cref="Page"/>, and <see cref="Model"/> values if they are non-null.
        /// </para>
        /// <para>The GetTemplateType delegate of the created template will be set to this
        /// RazorBit's <see cref="GetTemplateType"/> instance.</para>
        /// </remarks>
        public RbTemplate GetTemplateInstance(string name) {
            return GetTemplateInstance<RbTemplate>(name);
        }

        /// <summary>
        /// Compile a razor template from a string and optionally store it in the cache.
        /// </summary>
        /// <param name="razorCode">A string containing a Razor template.</param>
        /// <param name="name">A name through which to reference the template in the cache.</param>
        /// <returns>An <see cref="Type"/> representing the compiled Razor template.</returns>
        /// <exception cref="RbCompileException">Failed to compile the Razor template.</exception>
        /// <remarks>
        /// <para>This function may be used to compile templates to be run immediately and for
        /// templates to be inserted into the cache for later use through
        /// <see cref="GetTemplate(string)"/>
        /// </para>
        /// </remarks>
        public Type CompileTemplate(string razorCode, string? name = null) {
            var template = Engine.ProcessString(razorCode).Compile();
            if (name is not null)
                m_templateCache[name] = template;
            return template;
        }
    }

    /// <summary>
    /// Consolidated configuration information for RazorProjectEngine
    /// </summary>
    public class RbConfiguration {
        public RbConfiguration() {
            // Set Defaults
            NameSpace = "Bredd.Razor.Template";
            DefaultBaseClass = typeof(RbHtmlTemplate).FullName ?? throw new NullReferenceException("Unexpected null class FullName");
        }

        /// <summary>
        /// The directory from which Razor template files will be read. May be null. Defaults to null.
        /// </summary>
        /// <remarks>
        /// <para>If not null, a <see cref="RazorProjectFileSystem"/> will be created on the designated
        /// directory and supplied to the project.
        /// </para>
        /// <para>If null, and <see cref="FileSystem"/> is also null then <see cref="EmptyProjectFileSystem"/> will be used.
        /// </para>
        /// <para>If <see cref="FileSystem"/> is set, it takes precedence over this property.
        /// </para>
        /// </remarks>
        public string? SourceDirectory { get; set; }

        /// <summary>
        /// Set a custom file system derived from <see cref="RazorProjectFileSystem"/>. May be null. Defaults to null.
        /// </summary>
        /// <para>This is typically used if you are creating a custom file system sourcing Razor documents
        /// from a database, a zip file, or some source other than a file system directory. If this value is
        /// set it takes precedence over <see cref="SourceDirectory"/>.
        /// </para>
        /// <seealso cref="SourceDirectory"/>
        public RazorProjectFileSystem? FileSystem { get; set; }

        /// <summary>
        /// Namespace into which the Razor pages will be compiled. Defaults to "Razor".
        /// </summary>
        public string NameSpace { get; set; }

        /// <summary>
        /// Default base class for compiled razor pages. Defaults to "RbTemplateBase".
        /// </summary>
        /// <remarks>
        /// A template can override this with the @inherits directive.
        /// </remarks>
        public string DefaultBaseClass { get; set; }

        /// <summary>
        /// Callback to configure a Razor project engine with the configured and default values of this configuraiton instance.
        /// </summary>
        /// <param name="builder">The RazorProjectEngineBuilder to configure.</param>
        /// <remarks>This is not sensitive to the <see cref="SourceDirectory"/> setting.</remarks>
        public void ConfigureRazorProjectEngine(RazorProjectEngineBuilder builder) {
            //builder.SetRootNamespace("RootNamespace"); // I haven't been able to determine what this does
            builder.SetNamespace(NameSpace); // If FileKind is "component" then namespace will be "__GeneratedComponent" regardless of this setting.
            builder.ConfigureClass((document, node) => {
                node.BaseType = DefaultBaseClass;      // This can be overridden by the @inherits directive
                node.ClassName = "Template";    // This could be derived from the filename by using document.Source.FilePath;
            });
            // The following will add a new directive to be parsed (e.g. @mydirective Go). But making The directive do anything is a different task.
            // builder.AddDirective(DirectiveDescriptor.CreateSingleLineDirective("mydirective", b => b.AddMemberToken("memberTokenName", "memberTokenDescription")));
        }

        /// <summary>
        /// Create a <see cref="RazorProjectEngine"/> according to the configuration.
        /// </summary>
        /// <returns>A new RazorProjectEngine instance</returns>
        public RazorProjectEngine CreateEngine() {
            var fileSystem = FileSystem ??
                ((SourceDirectory is null)
                    ? EmptyProjectFileSystem.Instance
                    : RazorProjectFileSystem.Create(Path.GetFullPath(SourceDirectory)));
            return RazorProjectEngine.Create(RazorConfiguration.Default, fileSystem, ConfigureRazorProjectEngine);
        }
    }

    static class RbHelps {
        public static RazorCodeDocument ProcessFile(this RazorProjectEngine engine, string filename) {
            var item = engine.FileSystem.GetItem(filename, FileKinds.Legacy); // Making the FileKind always "mvc" gives consistent results regardless of the filename extension.
            if (!item.Exists)
                throw new FileNotFoundException("File not found in RazorProjectFileSystem", filename);
            return engine.Process(item);
        }

        public static RazorCodeDocument ProcessString(this RazorProjectEngine engine, string content) {
            string filename = Guid.NewGuid().ToString() + ".tmp";
            RazorSourceDocument sourceDoc = RazorSourceDocument.Create(content, new RazorSourceDocumentProperties(Path.Combine(filename, Path.GetTempPath()), filename));
            return engine.Process(sourceDoc, FileKinds.Legacy, null, null);
        }

        public static string GenerateCode(this RazorCodeDocument document) {
            return document.GetCSharpDocument().GeneratedCode;
        }

        static readonly CSharpCompilationOptions s_compilationOptions = new CSharpCompilationOptions(
            OutputKind.DynamicallyLinkedLibrary, reportSuppressedDiagnostics: false, optimizationLevel: OptimizationLevel.Release);

        static readonly MetadataReference[] s_metadataReferences = new MetadataReference[] {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(RbTemplate).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(DynamicObject).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(HttpUtility).Assembly.Location),
            //MetadataReference.CreateFromFile(typeof(Microsoft.AspNetCore.Razor.Hosting.RazorCompiledItemAttribute).Assembly.Location),
            MetadataReference.CreateFromFile(Assembly.Load(new AssemblyName("Microsoft.CSharp")).Location),
            MetadataReference.CreateFromFile(Assembly.Load(new AssemblyName("netstandard")).Location),
            MetadataReference.CreateFromFile(Assembly.Load(new AssemblyName("System.Runtime")).Location),
            MetadataReference.CreateFromFile(Assembly.Load(new AssemblyName("System.Collections")).Location),
        };

        public static Type Compile(this RazorCodeDocument document) {
            // TODO: Remove this after debugging is essentially finished.
            // Console.WriteLine(document.GetCSharpDocument().GeneratedCode);

            // Including the path argument makes sure the filename is included in error messages retrieved from the result.
            var syntaxTree = CSharpSyntaxTree.ParseText(document.GetCSharpDocument().GeneratedCode, path: document.Source.RelativePath);
            var metadataReferences = new List<MetadataReference>(s_metadataReferences);
			if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
				metadataReferences.Add(MetadataReference.CreateFromFile(Assembly.Load(new AssemblyName("netstandard")).Location));
			}
			var compilation = CSharpCompilation.Create("MyAssembly", [syntaxTree], metadataReferences, s_compilationOptions);

            var memoryStream = new MemoryStream();
            var result = compilation.Emit(memoryStream);
            if (!result.Success) {
                // TODO: Better error handling
                foreach (var diagnostic in result.Diagnostics) {
                    Console.WriteLine($"{diagnostic.ToString()}");
                    //Console.WriteLine($"{diagnostic.Severity}: {diagnostic.Location.Kind} {diagnostic.Location}  {diagnostic.GetMessage()}");
                }
                throw new Exception("Compilation failure.");
            }

            memoryStream.Position = 0;
            Assembly assembly = Assembly.Load(memoryStream.ToArray());
            return assembly.ExportedTypes.Single(); // Only one type exported from the compiled assembly.
        }

        /// <summary>
        /// Create an instance of the Razor template represented by <paramref name="templateType"/>.
        /// </summary>
        /// <typeparam name="TBaseType">The expected base type of the instantiated Razor template.</typeparam>
        /// <param name="templateType">A compiled Razor template.</param>
        /// <returns>The instantiated template.</returns>
        public static TBaseType InstantiateTemplate<TBaseType>(Type templateType) where TBaseType : class {
            if (!typeof(TBaseType).IsAssignableFrom(templateType))
                throw new RbRuntimeException($"Razor Template of type '{templateType.FullName}' is not based on expected type '{typeof(TBaseType).FullName}'");
            TBaseType? instance = Activator.CreateInstance(templateType) as TBaseType;
            if (instance == null)
                throw new NullReferenceException(); // this shouldn't happen
            return instance;
        }

        /// <summary>
        /// Create an <see cref="ExpandoObject"/> and load it with the public properties of the source object.
        /// </summary>
        /// <param name="obj">An object supplying the values for the Expando.</param>
        /// <returns>The created <see cref="ExpandoObject"/></returns>
        public static ExpandoObject ToExpando(object obj) {
            var expando = new ExpandoObject();
            IDictionary<string, object?> dict = expando;
            foreach (var pi in obj.GetType().GetProperties()) {
                dict.Add(pi.Name, pi.GetValue(obj));
            }
            return expando;
        }

        // TODO: See if this already exists somewhere
        public static ExpandoObject Clone(this ExpandoObject src) {
            var clone = new ExpandoObject();
            IDictionary<string, object?> srcDict = src;
            IDictionary<string, object?> dstDict = clone;
            foreach (KeyValuePair<string, object?> pair in srcDict) {
                dstDict.Add(pair.Key, pair.Value);
            }
            return clone;
        }

        public static string ToRelativePath(string fromPath, string toPath) {
            var fromParts = fromPath.Split('/', '\\');
            var toParts = toPath.Split("/", '\\');
            int same = 0;
            while (same < fromParts.Length - 1 && same < toParts.Length - 1 && string.Equals(fromParts[same], toParts[same], StringComparison.Ordinal))
                ++same;
            var sb = new StringBuilder();
            for (int i = 0; i < fromParts.Length - same - 1; ++i) {
                sb.Append("../");
            }
            for (int i = same; i < toParts.Length - 1; ++i) {
                sb.Append(toParts[i]);
                sb.Append('/');
            }
            sb.Append(toParts[toParts.Length - 1]);
            return sb.ToString();
        }
    }

    /// <summary>
    /// An empty file system for RazorProjectEngine
    /// </summary>
    /// <remarks>
    /// This is a near duplicate of a class within the
    /// Microsoft.AspNetCore.Razor.Language assembly. That class is set as
    /// 'internal' so it is not accessible to external callers.
    /// </remarks>
    internal class EmptyProjectFileSystem : RazorProjectFileSystem {
        public static readonly EmptyProjectFileSystem Instance = new EmptyProjectFileSystem();

        private EmptyProjectFileSystem() { }

        public override IEnumerable<RazorProjectItem> EnumerateItems(string basePath) {
            NormalizeAndEnsureValidPath(basePath);
            return Enumerable.Empty<RazorProjectItem>();
        }

        public override RazorProjectItem GetItem(string path) {
            return GetItem(path, FileKinds.Legacy);
        }

        public override RazorProjectItem GetItem(string path, string fileKind) {
            NormalizeAndEnsureValidPath(path);
            return new NotFoundProjectItem(string.Empty, path, fileKind);
        }

        internal class NotFoundProjectItem : RazorProjectItem {
            public override string BasePath { get; }
            public override string FilePath { get; }
            public override string FileKind { get; }
            public override bool Exists => false;
            public override string PhysicalPath {
                get {
                    throw new NotSupportedException();
                }
            }
            public NotFoundProjectItem(string basePath, string path, string fileKind) {
                BasePath = basePath;
                FilePath = path;
                FileKind = fileKind ?? FileKinds.GetFileKindFromFilePath(path);
            }
            public override Stream Read() {
                throw new NotSupportedException();
            }
        }
    }

    class RbRuntimeException : Exception {
        public RbRuntimeException(string message)
            : base(message) { }

        public RbRuntimeException(string message, Exception innerException)
            : base(message, innerException) { }
    }

    /// <summary>
    /// The base class for HTML Razor templates compiled by RazorBit.
    /// </summary>
    /// <remarks>
    /// <para>Contents are HTML encouded unless the Html.Raw() method is used./>
    /// </remarks>
    public abstract class RbHtmlTemplate : RbTemplate {
        static ISimpleHtmlHelper s_htmlHelper = new SimpleHtmlHelper();

        #region Exposed to Razor

        protected ISimpleHtmlHelper Html => s_htmlHelper;

        protected override void Write(object? value = null) {
            if (value is null) return;
            base.WriteLiteral(value is IHtmlString htmlString
                ? htmlString.ToHtmlString()
                : HttpUtility.HtmlEncode(value));
        }

        protected override void WriteAttributeValue(string prefix, int prefixOffset, object value, int valueOffset, int valueLength, bool isLiteral) {
            if (value is null) return;
            object? val = value is IHtmlString htmlString
                ? htmlString.ToHtmlString()
                : HttpUtility.HtmlAttributeEncode(value.ToString());
            base.WriteAttributeValue(prefix, prefixOffset, val ?? string.Empty, valueOffset, valueLength, isLiteral);
        }

        #endregion Exposed to Razor

        protected interface ISimpleHtmlHelper {
            IHtmlString Raw(string value);
            IHtmlString Raw(object value);
            IHtmlString UrlEncode(string value);
        }

        private class SimpleHtmlHelper : ISimpleHtmlHelper {
            public IHtmlString Raw(string value) {
                return new SimpleHtmlString(value);
            }

            public IHtmlString Raw(object value) {
                return new SimpleHtmlString(value.ToString());
            }

            public IHtmlString UrlEncode(string value) {
                return new SimpleHtmlString(HttpUtility.UrlEncode(value));
            }
        }

        private class SimpleHtmlString : IHtmlString {
            string? value;

            public SimpleHtmlString(string? value) {
                this.value = value;
            }

            public string? ToHtmlString() {
                return value;
            }

            public override string ToString() {
                return value ?? string.Empty
                ;
            }
        }
    }

    /// <summary>
    /// The base class for plain text Razor templates compiled by RazorBit.
    /// </summary>
    /// <remarks>
    /// <para>Presently this template adds nothing to the base class. That will likely
    /// change with future enhancements.</para>
    /// </remarks>
    public abstract class RbTextTemplate : RbTemplate {
    }

    /// <summary>
    /// The abstract base class for Razor templates compiled by RazorBit.
    /// </summary>
    public class RbTemplate {

        private dynamic? m_site;
        private dynamic? m_collection;
        private dynamic? m_page;
        private dynamic? m_model;
        private StringBuilder? m_bodyContent;
        Func<string, Type>? m_getTemplateType;
        private StringBuilder m_stringBuilder = new StringBuilder();
        private string? m_attributeSuffix = null;

        /// <summary>
        /// Set optional context for the template run.
        /// </summary>
        /// <param name="site">A <see cref="DynamicObject"/> representing the whole site.</param>
        /// <param name="page">A <see cref="DynamicObject"/> representing this page.</param>
        /// <param name="model">A <see cref="DynamicObject"/> representing the data model for this page.</param>
        /// <param name="bodyContent">A string representing a nested template to be included in a layout.</param>
        /// <param name="getTemplateType">A delegate function that can retrieve templates for Include or Layout.</param>
        /// <remarks>
        /// <para>The values provided here may be overridden by a subsequent call to <see cref="SetContext"/> or
        /// by values passed to <see cref="Run"/>. Null values are left unchanged.
        /// </para>
        /// <para>The <paramref name="site"/> and <paramref name="page"/> properties are patterned
        /// after the corresponding properties in Jekyll (see <see cref="https://jekyllrb.com"/>).
        /// The <paramref name="model"/> and <paramref name="content"/> properties follow the
        /// ASP Razor pattern. Depending on the host application the values may be read-only,
        /// read-write, or null. Likewise, the contents will depend on the host application.
        /// </para>
        /// </remarks>
        /// 
        public void SetContext(dynamic? site = null, dynamic? page = null,
            dynamic? model = null, dynamic? collection = null, StringBuilder? bodyContent = null,
            Func<string, Type>? getTemplateType = null) {

            if (site is not null) m_site = site;
            if (page is not null) m_page = page;
            if (model is not null) m_model = model;
            if (collection is not null) m_collection = collection;
            if (bodyContent is not null) m_bodyContent = bodyContent;
            if (getTemplateType is not null) m_getTemplateType = getTemplateType;
        }

        /// <summary>
        /// Render a razor script.
        /// </summary>
        /// <param name="site">A <see cref="DynamicObject"/> representing the whole site.</param>
        /// <param name="page">A <see cref="DynamicObject"/> representing this page.</param>
        /// <param name="model">A <see cref="DynamicObject"/> representing the data model for this page.</param>
        /// <param name="bodyContent">A string representing a nested template to be included in a layout.</param>
        /// <param name="getTemplateType">A delegate function that can retrieve templates for Include or Layout.</param>
        /// <remarks>
        /// <para>Any values provided here override those set by a previous call to <see cref="SetContext"/>.
        /// Null values are left unchanged.
        /// </para>
        /// <para>The <paramref name="site"/> and <paramref name="page"/> properties are patterned
        /// after the corresponding properties in Jekyll (see <see cref="https://jekyllrb.com"/>).
        /// The <paramref name="model"/> and <paramref name="content"/> properties follow the
        /// ASP Razor pattern. Depending on the host application the values may be read-only,
        /// read-write, or null. Likewise, the contents will depend on the host application.</para>
        /// </remarks>
        public StringBuilder Run(dynamic? site = null, dynamic? page = null,
            dynamic? model = null, StringBuilder? bodyContent = null,
            Func<string, Type>? getTemplateType = null) {

            m_stringBuilder = new StringBuilder();
            try {
                if (site is not null) m_site = site;
                if (page is not null) m_page = page;
                if (model is not null) m_model = model;
                if (bodyContent is not null) m_bodyContent = bodyContent;
                if (getTemplateType is not null) m_getTemplateType = getTemplateType;

                if (m_site is null) m_site = new ExpandoObject();
                if (m_page is null) m_page = new ExpandoObject();
                if (m_model is null) m_model = new ExpandoObject();

                ExecuteAsync().GetAwaiter().GetResult();

                if (Layout is not null) {
                    var layoutTemplate = InstantiateByName(Layout, $"Layout=\"{Layout}\"");
                    m_stringBuilder = layoutTemplate.Run(site: m_site, page: m_page, model: model, bodyContent: m_stringBuilder, getTemplateType: getTemplateType);
                }

                return m_stringBuilder;
            }
            // Error message augmentation
            catch (Microsoft.CSharp.RuntimeBinder.RuntimeBinderException ex) {
                throw new RbRuntimeException($"{ex.Message}: Object class may be private or otherwise inaccessible to Razor. Consider making it public or using RbHelps.ToExpando().", ex);
            }
        }

        #region Internal to class

        private RbTemplate InstantiateByName(string name, string context) {
            if (m_getTemplateType is null)
                throw new RbRuntimeException($"Error: {context}: No getTemplateType delegate specified");
            try {
                Type templateType = m_getTemplateType(name);
                var instance = (RbTemplate?)Activator.CreateInstance(templateType);
                if (instance == null)
                    throw new NullReferenceException();
                return instance;
            }
            catch (Exception err) {
                throw new RbRuntimeException($"Error: {context}: {err.Message}", err);
            }
        }

        #endregion Internal to class

        public virtual Task ExecuteAsync() {
            return Task.CompletedTask;
        }

        #region Exposed to Razor

        /// <summary>
        /// Site properties for use by the template. (See <see cref="RbTemplate.RbTemplateBase(dynamic, dynamic, dynamic, string)"/>)
        /// </summary>
        public dynamic? Site => m_site;

        /// <summary>
        /// Properties of the collection to which this page belongs
        /// </summary>
        public dynamic? Collection => m_collection;

        /// <summary>
        /// Site properties for use by the template (See <see cref="RbTemplate.RbTemplateBase(dynamic, dynamic, dynamic, string)"/>)
        /// </summary>
        public dynamic? Page => m_page;

        /// <summary>
        /// Site properties for use by the template (See <see cref="RbTemplate.RbTemplateBase(dynamic, dynamic, dynamic, string)"/>)
        /// </summary>
        public dynamic? Model => m_model;

        /// <summary>
        /// Converts a site-abosolute path into path relative to this page.
        /// </summary>
        /// <param name="sitePath">The site-absolute path to convert.</param>
        /// <returns>The relative path.</returns>
        /// <remarks>
        /// Page.Path must be set for this function to work.
        /// </remarks>
        public string ToRelativePath(string sitePath) {
            if (m_page is null) throw new NullReferenceException("ToRelativePath: No Page value has been set.");
            return RbHelps.ToRelativePath(m_page.Path, sitePath);
        }

        /// <summary>
        /// Converts an absolute or relative path to a global URL
        /// </summary>
        /// <param name="path">The absolute or relative path to convert.</param>
        /// <returns>The global URL</returns>
        public string ToAbsoluteUrl(string path) {
            Uri uri = new Uri(new Uri(Page!.Url), path);
            return uri.AbsoluteUri;
        }

        /// <summary>
        /// Render the body of a template wrapped in the layout.
        /// </summary>
        public Object? RenderBody() {
            if (m_bodyContent is null) return null;
            m_stringBuilder.Append(m_bodyContent);
            return null;
        }

        /// <summary>
        /// The name of a Layout template.
        /// </summary>
        /// <remarks>
        /// Layout will be null when the template is called. If the template sets a value, then
        /// its contents will be embedded in a layout template of the specified name.
        /// </remarks>
        protected string? Layout { get; set; }

        /// <summary>
        /// Render the specified template and embed its contents at this location in the template.
        /// </summary>
        /// <param name="name">The name of the template to render.</param>
        /// <param name="model">(Optional) A model to pass to the template.</param>
        /// <remarks>
        /// The called template will receive the same <see cref="Site"/> and <see cref="Page"/>
        /// values as the caller. The existing model, a new one, or nothing may be passed in
        /// for the <paramref name="model"/> value.
        /// </remarks>
        public Object? Include(string name, object? model = null) {
            var includeTemplate = InstantiateByName(name, $"Include(\"{name}\")");
            var content = includeTemplate.Run(site: m_site, page: m_page, model: model, getTemplateType: m_getTemplateType);
            m_stringBuilder.Append(content);
            return null;
        }

        protected virtual void WriteLiteral(string? literal = null) {
            m_stringBuilder.Append(literal);
        }

        protected virtual void Write(object? obj = null) {
            m_stringBuilder.Append(obj);
        }

        protected virtual void BeginWriteAttribute(string name, string prefix, int prefixOffset, string suffix, int suffixOffset, int attributeValuesCount) {
            m_attributeSuffix = suffix;
            m_stringBuilder.Append(prefix);
        }

        protected virtual void WriteAttributeValue(string prefix, int prefixOffset, object value, int valueOffset, int valueLength, bool isLiteral) {
            m_stringBuilder.Append(prefix);
            m_stringBuilder.Append(value);
        }

        protected virtual void EndWriteAttribute() {
            m_stringBuilder.Append(m_attributeSuffix);
            m_attributeSuffix = null;
        }

        #endregion Exposed to Razor
    }
}

// This is a kludge that avoids having a dependency on Microsoft.AspNetCore.Mvc.Razor.RuntimeCompilation
// The proper solution is to update the Razor compiler configuration so that it doesn't insert the dependencies in the first place
// Perhaps I'll eventually track that one down.
namespace Microsoft.AspNetCore.Razor.Hosting {
	[AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
	public sealed class RazorCompiledItemAttribute : Attribute {
		public RazorCompiledItemAttribute(Type type, string kind, string identifier) {
			Type = type;
			Kind = kind;
			Identifier = identifier;
		}

		public string Kind { get; private set; }
		public string Identifier { get; private set; }
		public Type Type { get; private set; }
	}

	[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
	public sealed class RazorSourceChecksumAttribute : Attribute {
		public RazorSourceChecksumAttribute(string checksumAlgorithm, string checksum, string identifier) {
			ChecksumAlgorithm = checksumAlgorithm;
			Checksum = checksum;
			Identifier = identifier;
		}

		public string Checksum { get; private set; }
		public string ChecksumAlgorithm { get; private set; }
		public string Identifier { get; private set; }
	}
}

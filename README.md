# Bollard

Bollard is a simple blog-aware static website generator inspired by [Jekyll](https://jekyllrb.com/).

**Like Jekyll, Bollard:**
* Lets you build from a mix of [MarkDown](https://markdown.github.io/) and HTML pages.
* Facilitates the use of layouts and includes to achieve a consistent look without repeating content.
* Includes metadata at the beginning of pages that can be incorporated into pages or to control conditional rendering. (Jekyll calls it "Front Matter", Bollard calls it "Metadata.")
* Includes collections that can be used to build blog or blog-like sites and subsites.

**Unlike Jekyll, Bollard:**
* Uses the powerful [Razor Syntax](https://learn.microsoft.com/en-us/aspnet/core/mvc/views/razor?view=aspnetcore-10.0) for rendering and formatting. (Jekyll uses the [Liquid Template Language](https://liquidjs.com/tutorials/intro-to-liquid.html))
* Includes image processing features. This lets you automatically process images into the right resolution and dimensions to fit your web page.
* Can make use of metadata in images and other multimedia files.

**Temporary Limitations:**
While fully functional, Bollard is still in early development and, accordingly, has certain limitations.

* Windows only: Currently it only runs on Windows due to certain library dependencies. It is built on the .Net Core platform and we intend to substitute cross-platform libraries in place of the current Windows-only components.
* No [SCSS](https://sass-lang.com/): A valuable Jekyll feature is its ability to compile SCSS into CSS stylesheets. We fully intend to add this feature to Bollard in due time.
* No documentation.
* Few samples.

## History

I ([bredd](https://github.com/bredd)) created an early version of Bollard to render [Brandt's Bollard Blog](https://bollard.brandtredd.org/). Just as Jekyll makes it easy to blog simply by writing an article in MarkDown, I wanted to do photo blogging by simply adding metadata to photos and uploading them to a folder. While I liked the simple and yet powerful nature of Jekyll, the lack of image processing capability made it unsuitable for the Bollard Blog project.

I am a proficient C# / .NET programmer and I am also a fan of the Razor syntax. While it is inspired by other HTML scripting languages like PHP and Liquid, Razor is simpler, safer (due to HTML encoding by default), and more powerful.

A little more than a year after deploying my first prototype to render the Bollard Blog, I decided to open-source the engine in early 2026. I hope I can attract others to both use and contribute to the project.

## Feature Backlog

I will soon begin using GitHub Projects to manage the feature backlog. But for now, this list will suffice:

1. Convert the _bollard_config.json file into a _bollard_config.cs file that is written in CSharp top-level-statement format. This may not make sense, at first, to use a programming language for configuration. But trust me, it will be both simple and powerful!
1. Make the Razor base class compatible with most of the commonly-used features available in ASP.NET (those that are appropriate for a static website).
1. Support C# codebehind the same way it works in ASP.NET.
1. Add support for Razor components.
1. Extend image processing to arbitrary images embedded in arbitrary pages; not just image collections that it now supports. This will likely be manifest as Razor components.
1. Add built-in web server with file system monitoring and auto-rebuild for testing websites.

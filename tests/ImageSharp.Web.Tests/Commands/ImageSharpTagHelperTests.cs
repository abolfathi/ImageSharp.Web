// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Microsoft.Extensions.WebEncoders.Testing;
using SixLabors.ImageSharp.Web.DependencyInjection;
using SixLabors.ImageSharp.Web.Middleware;
using Xunit;

namespace SixLabors.ImageSharp.Web.Tests.Commands
{
    public sealed class ImageSharpTagHelperTests : IDisposable
    {
        public ImageSharpTagHelperTests()
        {
            ServiceCollection services = new();
            services.AddSingleton<IWebHostEnvironment, FakeWebHostEnvironment>();
            services.AddImageSharp();
            this.Provider = services.BuildServiceProvider();
        }

        public ServiceProvider Provider { get; }

        [Theory]
        [InlineData(null, "test.jpg", "test.jpg")]
        [InlineData("abcd.jpg", "test.jpg", "test.jpg")]
        [InlineData(null, "~/test.jpg", "virtualRoot/test.jpg")]
        [InlineData("abcd.jpg", "~/test.jpg", "virtualRoot/test.jpg")]
        public void Process_SrcDefaultsToTagHelperOutputSrcAttributeAddedByOtherTagHelper(
            string src,
            string srcOutput,
            string expectedSrcPrefix)
        {
            // Arrange
            var allAttributes = new TagHelperAttributeList(
                new TagHelperAttributeList
                {
                    { "alt", new HtmlString("Testing") },
                    { "width", 100 },
                });
            TagHelperContext context = MakeTagHelperContext(allAttributes);
            var outputAttributes = new TagHelperAttributeList
                {
                    { "alt", new HtmlString("Testing") },
                    { "src", srcOutput },
                };
            var output = new TagHelperOutput(
                "img",
                outputAttributes,
                getChildContentAsync: (useCachedResult, encoder) => Task.FromResult<TagHelperContent>(
                    new DefaultTagHelperContent()));

            var urlHelperFactory = new FakeUrlHelperFactory();

            ImageSharpTagHelper helper = this.GetHelper(urlHelperFactory: urlHelperFactory);
            helper.Src = src;
            helper.Width = 100;

            // Act
            helper.Process(context, output);

            // Assert
            Assert.Equal(
                expectedSrcPrefix + "?width=100",
                (string)output.Attributes["src"].Value,
                StringComparer.Ordinal);
        }

        [Fact]
        public void PreservesOrderOfSourceAttributesWhenRun()
        {
            // Arrange
            TagHelperContext context = MakeTagHelperContext(
                attributes: new TagHelperAttributeList
                {
                    { "alt", new HtmlString("alt text") },
                    { "data-extra", new HtmlString("something") },
                    { "title", new HtmlString("Image title") },
                    { "src", "testimage.png" },
                    { "width", 50 },
                    { "height", 60 }
                });

            TagHelperOutput output = MakeImageTagHelperOutput(
                attributes: new TagHelperAttributeList
                {
                    { "alt", new HtmlString("alt text") },
                    { "data-extra", new HtmlString("something") },
                    { "title", new HtmlString("Image title") },
                    { "width", 50 },
                    { "height", 60 }
                });

            TagHelperOutput expectedOutput = MakeImageTagHelperOutput(
                attributes: new TagHelperAttributeList
                {
                    { "alt", new HtmlString("alt text") },
                    { "data-extra", new HtmlString("something") },
                    { "title", new HtmlString("Image title") },
                    { "src", "testimage.png?width=50&height=60" },
                    { "width", 50 },
                    { "height", 60 }
                });

            ImageSharpTagHelper helper = this.GetHelper();
            helper.Src = "testimage.png";

            // Act
            helper.Process(context, output);

            // Assert
            Assert.Equal(expectedOutput.TagName, output.TagName);
            Assert.Equal(6, output.Attributes.Count);

            for (int i = 0; i < expectedOutput.Attributes.Count; i++)
            {
                TagHelperAttribute expectedAttribute = expectedOutput.Attributes[i];
                TagHelperAttribute actualAttribute = output.Attributes[i];
                Assert.Equal(expectedAttribute.Name, actualAttribute.Name);
                Assert.Equal(expectedAttribute.Value.ToString(), actualAttribute.Value.ToString());
            }
        }

        [Fact]
        public void RendersImageTag_AddsAttributes_WithRequestPathBase()
        {
            // Arrange
            TagHelperContext context = MakeTagHelperContext(
                attributes: new TagHelperAttributeList
                {
                    { "alt", new HtmlString("alt text") },
                    { "src", "/bar/images/image.jpg" },
                    { "width", 100 },
                    { "height", 200 },
                });
            TagHelperOutput output = MakeImageTagHelperOutput(attributes: new TagHelperAttributeList
            {
                { "alt", new HtmlString("alt text") },
            });

            ViewContext viewContext = MakeViewContext("/bar");

            ImageSharpTagHelper helper = this.GetHelper();
            helper.Src = "/bar/images/image.jpg";
            helper.Width = 100;
            helper.Height = 200;

            // Act
            helper.Process(context, output);

            // Assert
            Assert.True(output.Content.GetContent().Length == 0);
            Assert.Equal("img", output.TagName);
            Assert.Equal(4, output.Attributes.Count);
            TagHelperAttribute srcAttribute = Assert.Single(output.Attributes, attr => attr.Name.Equals("src"));
            Assert.Equal("/bar/images/image.jpg?width=100&height=200", srcAttribute.Value);
        }

        private ImageSharpTagHelper GetHelper(
            IUrlHelperFactory urlHelperFactory = null,
            ViewContext viewContext = null)
        {
            urlHelperFactory ??= new FakeUrlHelperFactory();
            viewContext ??= MakeViewContext();

            return new ImageSharpTagHelper(
                Options.Create<ImageSharpMiddlewareOptions>(new()),
                this.Provider.GetRequiredService<RequestAuthorizationUtilities>(),
                urlHelperFactory,
                new HtmlTestEncoder())
            {
                ViewContext = viewContext,
            };
        }

        private static TagHelperContext MakeTagHelperContext(
            TagHelperAttributeList attributes)
            => new(
                tagName: "image",
                allAttributes: attributes,
                items: new Dictionary<object, object>(),
                uniqueId: Guid.NewGuid().ToString("N"));

        private static TagHelperOutput MakeImageTagHelperOutput(TagHelperAttributeList attributes)
        {
            attributes ??= new TagHelperAttributeList();

            return new TagHelperOutput(
                "img",
                attributes,
                getChildContentAsync: (useCachedResult, encoder) =>
                {
                    var tagHelperContent = new DefaultTagHelperContent();
                    tagHelperContent.SetContent(default);
                    return Task.FromResult<TagHelperContent>(tagHelperContent);
                });
        }

        private static ViewContext MakeViewContext(string requestPathBase = null)
        {
            var actionContext = new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor());
            if (requestPathBase != null)
            {
                actionContext.HttpContext.Request.PathBase = new PathString(requestPathBase);
            }

            var metadataProvider = new EmptyModelMetadataProvider();
            var viewData = new ViewDataDictionary(metadataProvider, new ModelStateDictionary());
            return new ViewContext(
                actionContext,
                new FakeView(),
                viewData,
                new FakeTempDataDictionary(),
                TextWriter.Null,
                new HtmlHelperOptions());
        }

        public void Dispose() => this.Provider.Dispose();

        private class FakeView : IView
        {
            public string Path { get; }

            public Task RenderAsync(ViewContext context) => throw new NotSupportedException();
        }

        private class FakeTempDataDictionary : Dictionary<string, object>, ITempDataDictionary
        {
            public void Keep() => throw new NotSupportedException();

            public void Keep(string key) => throw new NotSupportedException();

            public void Load() => throw new NotSupportedException();

            public object Peek(string key) => throw new NotSupportedException();

            public void Save() => throw new NotSupportedException();
        }

        private class FakeWebHostEnvironment : IWebHostEnvironment
        {
            public string WebRootPath { get; set; }

            public IFileProvider WebRootFileProvider { get; set; } = new FakeFileProvider();

            public string ApplicationName { get; set; }

            public IFileProvider ContentRootFileProvider { get; set; }

            public string ContentRootPath { get; set; }

            public string EnvironmentName { get; set; }
        }

        private class FakeFileProvider : IFileProvider
        {
            public IDirectoryContents GetDirectoryContents(string subpath) => new FakeDirectoryContents();

            public IFileInfo GetFileInfo(string subpath) => new FakeFileInfo();

            public IChangeToken Watch(string filter) => new FakeFileChangeToken();
        }

        private class FakeFileChangeToken : IChangeToken
        {
            public FakeFileChangeToken(string filter = "") => this.Filter = filter;

            public bool ActiveChangeCallbacks => false;

            public bool HasChanged { get; set; }

            public string Filter { get; }

            public IDisposable RegisterChangeCallback(Action<object> callback, object state) => new NullDisposable();

            private sealed class NullDisposable : IDisposable
            {
                public void Dispose()
                {
                }
            }

            public override string ToString() => this.Filter;
        }

        private class FakeDirectoryContents : IDirectoryContents
        {
            public bool Exists { get; }

            public IEnumerator<IFileInfo> GetEnumerator() => Enumerable.Empty<IFileInfo>().GetEnumerator();

            IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();
        }

        private class FakeFileInfo : IFileInfo
        {
            public bool Exists { get; } = true;

            public bool IsDirectory { get; }

            public DateTimeOffset LastModified { get; }

            public long Length { get; }

            public string Name { get; }

            public string PhysicalPath { get; }

            public Stream CreateReadStream() => new MemoryStream(Encoding.UTF8.GetBytes("Hello World!"));
        }

        private class FakeUrlHelperFactory : IUrlHelperFactory
        {
            public IUrlHelper GetUrlHelper(ActionContext context) => new FakeUrlHelper() { ActionContext = context };
        }

        private class FakeUrlHelper : IUrlHelper
        {
            public ActionContext ActionContext { get; set; }

            public string Action(UrlActionContext actionContext) => throw new NotSupportedException();

            // Ensure expanded path does not look like an absolute path on Linux, avoiding
            // https://github.com/aspnet/External/issues/21
            [return: NotNullIfNotNull("contentPath")]
            public string Content(string contentPath) => contentPath.Replace("~/", "virtualRoot/");

            public bool IsLocalUrl([NotNullWhen(true)] string url) => throw new NotSupportedException();

            public string Link(string routeName, object values) => throw new NotSupportedException();

            public string RouteUrl(UrlRouteContext routeContext) => throw new NotSupportedException();
        }
    }
}

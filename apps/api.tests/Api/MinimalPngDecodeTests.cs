using System;
using System.Threading.Tasks;
using FluentAssertions;
using SixLabors.ImageSharp;
using Xunit;

namespace Appostolic.Api.Tests.Api;

public class MinimalPngDecodeTests
{
    [Fact]
    public async Task MinimalPng_1x1_Decodes_With_ImageSharp()
    {
        var pngBytes = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAACXBIWXMAAA7EAAAOxAGVKw4bAAAAC0lEQVR4nGNhAAIAABkABaSlNawAAAAASUVORK5CYII=");

        using var ms = new System.IO.MemoryStream(pngBytes);
        var image = await Image.LoadAsync(ms);
        image.Width.Should().Be(1);
        image.Height.Should().Be(1);
    }
}

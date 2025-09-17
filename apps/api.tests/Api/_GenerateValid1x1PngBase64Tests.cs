using System;
using System.Threading.Tasks;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Formats.Png;
using Xunit;

namespace Appostolic.Api.Tests.Api;

// TEMPORARY: Used to generate a valid 1x1 PNG base64 for avatar test. Remove after updating constants.
public class _GenerateValid1x1PngBase64Tests
{
    [Fact]
    public async Task Print_Base64_For_Valid_1x1_Png()
    {
        using var img = new Image<Rgba32>(1,1);
        using var ms = new System.IO.MemoryStream();
        await img.SaveAsync(ms, new PngEncoder());
        var bytes = ms.ToArray();
        var b64 = Convert.ToBase64String(bytes);
        // Write to console so we can capture from test output
        Console.WriteLine("VALID_1x1_PNG_BASE64:" + b64);
        // Quick sanity: attempt decode
        ms.Position = 0;
        var decoded = await Image.LoadAsync(ms);
        Assert.Equal(1, decoded.Width);
        Assert.Equal(1, decoded.Height);
    }
}

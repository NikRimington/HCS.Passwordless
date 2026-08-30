using HCS.Passwordless.WebAuthn.Configuration;
using HCS.Passwordless.WebAuthn.Controllers;
using Microsoft.AspNetCore.Mvc;

namespace HCS.Passwordless.Tests.WebAuthn;

public class WellKnownWebAuthnControllerTests
{
    private readonly IOptionsMonitor<WebAuthnOptions> _monitor =
        Substitute.For<IOptionsMonitor<WebAuthnOptions>>();

    private WellKnownWebAuthnController CreateSut() => new(_monitor);

    private void SetupOptions(WebAuthnOptions opts) =>
        _monitor.CurrentValue.Returns(opts);

    [Fact]
    public void GetRelatedOrigins_WhenDisabled_ReturnsNotFound()
    {
        SetupOptions(new WebAuthnOptions { Enabled = false, Origins = ["https://example.com"] });

        CreateSut().GetRelatedOrigins().Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public void GetRelatedOrigins_WhenOriginsIsNull_ReturnsNotFound()
    {
        SetupOptions(new WebAuthnOptions { Enabled = true, Origins = null! });

        CreateSut().GetRelatedOrigins().Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public void GetRelatedOrigins_WhenOriginsIsEmpty_ReturnsNotFound()
    {
        SetupOptions(new WebAuthnOptions { Enabled = true });

        CreateSut().GetRelatedOrigins().Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public void GetRelatedOrigins_WhenAllOriginsUseHttp_ReturnsNotFound()
    {
        SetupOptions(new WebAuthnOptions { Enabled = true, Origins = ["http://example.com", "http://another.com"] });

        CreateSut().GetRelatedOrigins().Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public void GetRelatedOrigins_WhenAllOriginsAreRelativeOrInvalid_ReturnsNotFound()
    {
        SetupOptions(new WebAuthnOptions { Enabled = true, Origins = ["/relative", "not-a-uri", ""] });

        CreateSut().GetRelatedOrigins().Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public void GetRelatedOrigins_WhenHttpsOriginHasNoHost_ReturnsNotFound()
    {
        SetupOptions(new WebAuthnOptions { Enabled = true, Origins = ["https://"] });

        CreateSut().GetRelatedOrigins().Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public void GetRelatedOrigins_WithSingleValidHttpsOrigin_ReturnsOkWithOrigin()
    {
        SetupOptions(new WebAuthnOptions { Enabled = true, Origins = ["https://example.com"] });

        var result = CreateSut().GetRelatedOrigins();

        result.Should().BeOfType<OkObjectResult>().Which.Value
            .Should().BeEquivalentTo(new { origins = new[] { "https://example.com" } });
    }

    [Fact]
    public void GetRelatedOrigins_NormalizesOriginsToLowercase()
    {
        // Collection expression creates a case-sensitive HashSet, so the upper-case entry is stored as-is.
        SetupOptions(new WebAuthnOptions { Enabled = true, Origins = ["HTTPS://EXAMPLE.COM"] });

        var result = CreateSut().GetRelatedOrigins();

        result.Should().BeOfType<OkObjectResult>().Which.Value
            .Should().BeEquivalentTo(new { origins = new[] { "https://example.com" } });
    }

    [Fact]
    public void GetRelatedOrigins_DeduplicatesOriginsAfterNormalization()
    {
        // A case-sensitive HashSet accepts both variants; both normalize to the same string,
        // so Distinct() must collapse them to a single entry.
        SetupOptions(new WebAuthnOptions
        {
            Enabled = true,
            Origins = new HashSet<string> { "https://example.com", "HTTPS://EXAMPLE.COM" }
        });

        var result = CreateSut().GetRelatedOrigins();

        result.Should().BeOfType<OkObjectResult>().Which.Value
            .Should().BeEquivalentTo(new { origins = new[] { "https://example.com" } });
    }

    [Fact]
    public void GetRelatedOrigins_WithMixedValidAndInvalidOrigins_ReturnsOnlyValidHttpsOrigins()
    {
        SetupOptions(new WebAuthnOptions
        {
            Enabled = true,
            Origins =
            [
                "https://valid.example.com",
                "http://insecure.example.com",
                "not-a-uri",
                "https://another.valid.com"
            ]
        });

        var result = CreateSut().GetRelatedOrigins();

        result.Should().BeOfType<OkObjectResult>().Which.Value
            .Should().BeEquivalentTo(new
            {
                origins = new[] { "https://valid.example.com", "https://another.valid.com" }
            });
    }
}

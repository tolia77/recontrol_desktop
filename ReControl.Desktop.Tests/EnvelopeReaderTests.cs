using System.Text.Json;
using FluentAssertions;
using ReControl.Desktop.Services;
using Xunit;

namespace ReControl.Desktop.Tests;

public class EnvelopeReaderTests
{
    [Fact]
    public void TryGetData_returns_data_element_on_success_envelope()
    {
        using var doc = JsonDocument.Parse(
            "{\"data\":{\"access_token\":\"Bearer abc\",\"user_id\":\"u1\"},\"meta\":null,\"error\":null}");
        EnvelopeReader.TryGetData(doc.RootElement, out var data).Should().BeTrue();
        data.GetProperty("access_token").GetString().Should().Be("Bearer abc");
    }

    [Fact]
    public void TryGetError_parses_error_object()
    {
        using var doc = JsonDocument.Parse(
            "{\"data\":null,\"meta\":null,\"error\":{\"code\":\"unauthorized\",\"message\":\"Invalid email or password\",\"details\":{}}}");
        var err = EnvelopeReader.TryGetError(doc.RootElement);
        err.Should().NotBeNull();
        err!.Code.Should().Be("unauthorized");
        err.Message.Should().Be("Invalid email or password");
    }

    [Fact]
    public void TryGetError_returns_null_when_no_error()
    {
        using var doc = JsonDocument.Parse("{\"data\":{},\"meta\":null,\"error\":null}");
        EnvelopeReader.TryGetError(doc.RootElement).Should().BeNull();
    }

    [Fact]
    public void TryGetData_returns_false_when_data_is_null()
    {
        using var doc = JsonDocument.Parse("{\"data\":null,\"meta\":null,\"error\":{\"code\":\"x\",\"message\":\"y\",\"details\":{}}}");
        EnvelopeReader.TryGetData(doc.RootElement, out _).Should().BeFalse();
    }
}

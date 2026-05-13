using System.Text;
using Xunit;
using ZeroNative.Primitives;

namespace ZeroNative.Tests;

public class JsonUtilitiesTests
{
    [Fact]
    public void EncodeString_EscapesSpecialCharacters()
    {
        Assert.Equal("\"hello \\\"world\\\"\"", JsonUtilities.EncodeString("hello \"world\""));
        Assert.Equal("\"a\\\\b\"", JsonUtilities.EncodeString("a\\b"));
        Assert.Equal("\"line\\nbreak\"", JsonUtilities.EncodeString("line\nbreak"));
        Assert.Equal("\"tab\\there\"", JsonUtilities.EncodeString("tab\there"));
    }

    [Fact]
    public void EncodeString_EscapesControlCharactersAsUnicode()
    {
        var input = new string(new[] { (char)1, (char)2, 'A' });
        var encoded = JsonUtilities.EncodeString(input);
        Assert.Equal("\"\\u0001\\u0002A\"", encoded);
    }

    [Fact]
    public void IsValidValue_RecognizesWellFormedJson()
    {
        Assert.True(JsonUtilities.IsValidValue("{\"a\":1}"));
        Assert.True(JsonUtilities.IsValidValue("\"string\""));
        Assert.True(JsonUtilities.IsValidValue("[1,2,3]"));
        Assert.True(JsonUtilities.IsValidValue("null"));
        Assert.True(JsonUtilities.IsValidValue("true"));
        Assert.True(JsonUtilities.IsValidValue("42"));
    }

    [Fact]
    public void IsValidValue_RejectsMalformedInput()
    {
        Assert.False(JsonUtilities.IsValidValue("raw text"));
        Assert.False(JsonUtilities.IsValidValue("{\"missing\":"));
        Assert.False(JsonUtilities.IsValidValue(""));
    }

    [Fact]
    public void StringField_ReadsTopLevelStringField()
    {
        var payload = """{"label":"main","title":"Main"}""";
        Assert.Equal("main", JsonUtilities.StringField(payload, "label"));
        Assert.Equal("Main", JsonUtilities.StringField(payload, "title"));
        Assert.Null(JsonUtilities.StringField(payload, "missing"));
    }

    [Fact]
    public void NumberField_ReadsNumbers()
    {
        var payload = """{"width":320,"height":240.5}""";
        Assert.Equal(320f, JsonUtilities.NumberField(payload, "width"));
        Assert.Equal(240.5f, JsonUtilities.NumberField(payload, "height"));
        Assert.Null(JsonUtilities.NumberField(payload, "missing"));
    }

    [Fact]
    public void BoolField_ReadsBooleans()
    {
        var payload = """{"open":true,"focused":false}""";
        Assert.True(JsonUtilities.BoolField(payload, "open"));
        Assert.False(JsonUtilities.BoolField(payload, "focused"));
        Assert.Null(JsonUtilities.BoolField(payload, "missing"));
    }

    [Fact]
    public void StringField_HandlesUnicodeEscapes()
    {
        var payload = """{"name":"étoile"}""";
        Assert.Equal("étoile", JsonUtilities.StringField(payload, "name"));
    }

    [Fact]
    public void StringField_DoesNotReadNestedFields()
    {
        var payload = """{"outer":{"inner":"deep"},"outer_label":"top"}""";
        Assert.Null(JsonUtilities.StringField(payload, "inner"));
        Assert.Equal("top", JsonUtilities.StringField(payload, "outer_label"));
    }

    [Fact]
    public void AppendString_WritesIntoExistingStringBuilder()
    {
        var sb = new StringBuilder("prefix:");
        JsonUtilities.AppendString(sb, "value");
        Assert.Equal("prefix:\"value\"", sb.ToString());
    }
}

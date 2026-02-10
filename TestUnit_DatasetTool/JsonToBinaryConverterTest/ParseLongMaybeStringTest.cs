using System;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Xunit;

namespace TestUnit_DatasetTool.JsonToBinaryConverterTest;

public class ParseLongMaybeStringTests
{
    private static long Call(ref Utf8JsonReader reader)
    {
        // Copie locale si la méthode est private
        return reader.TokenType switch
        {
            JsonTokenType.String => long.Parse(reader.GetString()!, CultureInfo.InvariantCulture),
            JsonTokenType.Number => reader.GetInt64(),
            _ => throw new FormatException($"Nombre invalide: {reader.TokenType}")
        };
    }

    [Fact]
    public void Parse_Number_Token()
    {
        // Arrange
        var json = "123456";
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        reader.Read(); // Positionne sur Number

        // Act
        var result = Call(ref reader);

        // Assert
        Assert.Equal(123456L, result);
    }

    [Fact]
    public void Parse_String_Token()
    {
        // Arrange
        var json = "\"123456\"";
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        reader.Read(); // Positionne sur String

        // Act
        var result = Call(ref reader);

        // Assert
        Assert.Equal(123456L, result);
    }

    [Fact]
    public void Parse_Negative_Number()
    {
        var json = "-42";
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        reader.Read();

        var result = Call(ref reader);

        Assert.Equal(-42L, result);
    }


    [Fact]
    public void Parse_String_Invalid_Format_Throws()
    {
        var json = "\"abc\"";
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        reader.Read();

        try
        {
            Call(ref reader);
            Assert.True(false, "Une FormatException était attendue");
        }
        catch (FormatException)
        {
            // OK
        }
    }

    [Fact]
    public void Parse_Invalid_Token_Throws_With_Message()
    {
        var json = "true";
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        reader.Read(); // Bool

        try
        {
            Call(ref reader);
            Assert.True(false, "Une FormatException était attendue");
        }
        catch (FormatException ex)
        {
            Assert.Contains("Nombre invalide", ex.Message);
            Assert.Contains("True", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
    }
}
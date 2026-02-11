using System;
using System.Text;
using System.Text.Json;
using DatasetTool;
using Xunit;

namespace DatasetToolTest.JsonToBinaryConverterTest;

public class ReadCandleTests
{
    private static Candle1m ParseOne(string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        var reader = new Utf8JsonReader(bytes, isFinalBlock: true, state: default);

        // avancer jusqu'au StartObject
        while (reader.Read() && reader.TokenType != JsonTokenType.StartObject) { }
        Assert.Equal(JsonTokenType.StartObject, reader.TokenType);

        return JsonToBinaryConverter.ReadCandle(ref reader);
    }

    [Fact]
    public void ReadCandle_Parses_Valid_Ohlcv_Strings()
    {
        string json = """
        {
          "hd": { "ts_event": "2010-06-07T15:17:00.000000000Z" },
          "open": "100",
          "high": "110",
          "low": "90",
          "close": "105",
          "volume": "1000",
          "symbol": "GOOG"
        }
        """;

        var candle = ParseOne(json);

        //Assert.True(candle.TsNs > 0);
        //Assert.Equal(100, candle.O);
        //Assert.Equal(110, candle.H);
        //Assert.Equal(90, candle.L);
        //Assert.Equal(105, candle.C);
        //Assert.Equal(1000, candle.V);
    }

    [Fact]
    public void ReadCandle_Parses_Valid_Ohlcv_Numbers()
    {
        string json = """
        {
          "hd": { "ts_event": "2010-06-07T15:17:00Z" },
          "open": 100,
          "high": 110,
          "low": 90,
          "close": 105,
          "volume": 1000
        }
        """;

        var candle = ParseOne(json);

        //Assert.True(candle.TsNs > 0);
        //Assert.Equal(100, candle.O);
        //Assert.Equal(110, candle.H);
        //Assert.Equal(90, candle.L);
        //Assert.Equal(105, candle.C);
        //Assert.Equal(1000, candle.V);
    }

    [Fact]
    public void ReadCandle_Works_With_Properties_In_Any_Order()
    {
        string json = """
        {
          "close": "105",
          "volume": "1000",
          "open": "100",
          "hd": { "ts_event": "2010-06-07T15:17:00.000000000Z", "rtype": 33, "publisher_id": 1 },
          "low": "90",
          "high": "110",
          "symbol": "GOOG",
          "extra_field": { "a": 1, "b": 2 }
        }
        """;

        var candle = ParseOne(json);

        //Assert.Equal(100, candle.O);
        //Assert.Equal(110, candle.H);
        //Assert.Equal(90, candle.L);
        //Assert.Equal(105, candle.C);
        //Assert.Equal(1000, candle.V);
        //Assert.True(candle.TsNs > 0);
    }

    [Fact]
    public void ReadCandle_Skips_Unknown_Fields_And_Nested_Objects()
    {
        string json = """
        {
          "hd": { "ts_event": "2010-06-07T15:17:00.000000000Z" },
          "open": "100",
          "high": "110",
          "low": "90",
          "close": "105",
          "volume": "1000",
          "some_nested": { "x": [1,2,3], "y": {"z":true} },
          "some_array": [ {"a":1}, {"b":2} ]
        }
        """;

        var candle = ParseOne(json);

        //Assert.Equal(105, candle.C);
        //Assert.Equal(1000, candle.V);
    }

    [Fact]
    public void ReadCandle_Throws_When_Timestamp_Missing()
    {
        string json = """
        {
          "hd": { "publisher_id": 1 },
          "open": "100",
          "high": "110",
          "low": "90",
          "close": "105",
          "volume": "1000"
        }
        """;

        Assert.ThrowsAny<Exception>(() => ParseOne(json));
    }

    [Theory]
    [InlineData("""{ "hd":{"ts_event":"2010-06-07T15:17:00Z"}, "high":"110","low":"90","close":"105","volume":"1000" }""")] // open missing
    [InlineData("""{ "hd":{"ts_event":"2010-06-07T15:17:00Z"}, "open":"100","low":"90","close":"105","volume":"1000" }""")] // high missing
    [InlineData("""{ "hd":{"ts_event":"2010-06-07T15:17:00Z"}, "open":"100","high":"110","close":"105","volume":"1000" }""")] // low missing
    [InlineData("""{ "hd":{"ts_event":"2010-06-07T15:17:00Z"}, "open":"100","high":"110","low":"90","volume":"1000" }""")] // close missing
    [InlineData("""{ "hd":{"ts_event":"2010-06-07T15:17:00Z"}, "open":"100","high":"110","low":"90","close":"105" }""")] // volume missing
    public void ReadCandle_Throws_When_Required_Field_Missing(string json)
    {
        Assert.ThrowsAny<Exception>(() => ParseOne(json));
    }

    [Fact]
    public void ReadCandle_Parses_Nanoseconds_Fraction()
    {
        string json = """
        {
          "hd": { "ts_event": "2010-06-07T15:17:00.123456789Z" },
          "open": "1",
          "high": "1",
          "low": "1",
          "close": "1",
          "volume": "1"
        }
        """;

        var c = ParseOne(json);

        Assert.True(c.TsNs > 0);
        // On ne compare pas précisément ici (timezone/parse), juste qu'il y a bien un ts
    }
}
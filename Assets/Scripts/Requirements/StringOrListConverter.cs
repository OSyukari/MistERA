using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>
/// Reads a List&lt;string&gt; field that may be authored (or saved by older versions) as a single string.
/// "" and null read as an empty list. Always writes an array.
/// </summary>
public class StringOrListConverter : JsonConverter
{
    public override bool CanConvert(Type objectType)
    {
        return objectType == typeof(List<string>);
    }

    public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
    {
        var list = new List<string>();
        if (reader.TokenType == JsonToken.Null) return list;
        if (reader.TokenType == JsonToken.String)
        {
            var s = (string)reader.Value;
            if (!string.IsNullOrEmpty(s)) list.Add(s);
            return list;
        }
        var token = JToken.Load(reader);
        if (token.Type == JTokenType.Array)
        {
            foreach (var t in token)
            {
                if (t.Type == JTokenType.Null) continue;
                var s = t.ToString();
                if (!string.IsNullOrEmpty(s)) list.Add(s);
            }
        }
        return list;
    }

    public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
    {
        var list = value as List<string>;
        writer.WriteStartArray();
        if (list != null) foreach (var s in list) writer.WriteValue(s);
        writer.WriteEndArray();
    }
}

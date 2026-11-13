using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MistEra.Updates
{
    public static class UpdateJson
    {
        private const int MaximumJsonBytes = 32 * 1024 * 1024;
        private static JsonSerializer Serializer() => JsonSerializer.Create(new JsonSerializerSettings
        {
            TypeNameHandling = TypeNameHandling.None,
            MetadataPropertyHandling = MetadataPropertyHandling.Ignore,
            MissingMemberHandling = MissingMemberHandling.Error,
            MaxDepth = 64,
            Culture = System.Globalization.CultureInfo.InvariantCulture
        });

        public static T Read<T>(string path)
        {
            UpdateValidation.NoLinks(path);
            UpdateValidation.Require(new FileInfo(path).Length <= MaximumJsonBytes, "JSON too large");
            return Parse<T>(File.ReadAllText(path, new UTF8Encoding(false, true)));
        }

        public static T Parse<T>(string json)
        {
            UpdateValidation.Require(json != null && Encoding.UTF8.GetByteCount(json) <= MaximumJsonBytes, "JSON too large/null");
            CheckSyntax(json);
            using var reader = new JsonTextReader(new StringReader(json)) { MaxDepth = 64, DateParseHandling = DateParseHandling.None };
            JToken token = JToken.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error, CommentHandling = CommentHandling.Load });
            UpdateValidation.Require(token.Type == JTokenType.Object && !reader.Read(), "Expected one JSON object");
            CheckTokens(token);
            CheckShape(token, typeof(T));
            T value = token.ToObject<T>(Serializer());
            Validate(value);
            return value;
        }

        private static void CheckSyntax(string json)
        {
            bool quoted = false, escaped = false;
            char previous = '\0';
            var literal = new StringBuilder();
            foreach (char c in json)
            {
                if (quoted)
                {
                    if (escaped) escaped = false;
                    else if (c == '\\') escaped = true;
                    else if (c == '"') quoted = false;
                    else UpdateValidation.Require(c >= 32, "Unescaped control character in JSON");
                    continue;
                }
                if (c == ' ' || c == '\r' || c == '\n' || c == '\t') { CheckLiteral(); continue; }
                UpdateValidation.Require(c != '\'' && c != '/', "Nonstandard JSON quoting/comments");
                UpdateValidation.Require(!((c == '}' || c == ']') && previous == ','), "Trailing JSON comma");
                if (c == '"') { CheckLiteral(); quoted = true; }
                else if ("{}[],:".Contains(c)) CheckLiteral();
                else literal.Append(c);
                previous = c;
            }
            CheckLiteral();
            using var reader = new JsonTextReader(new StringReader(json)) { MaxDepth = 64, DateParseHandling = DateParseHandling.None };
            while (reader.Read())
            {
                UpdateValidation.Require(reader.TokenType != JsonToken.Comment && reader.TokenType != JsonToken.Undefined && reader.TokenType != JsonToken.Float, "Nonstandard JSON token");
                if (reader.TokenType == JsonToken.PropertyName || reader.TokenType == JsonToken.String) UpdateValidation.Require(reader.QuoteChar == '"', "Unquoted JSON name/string");
            }

            void CheckLiteral()
            {
                if (literal.Length == 0) return;
                string text = literal.ToString();
                UpdateValidation.Require(text == "true" || text == "false" || text == "null" || System.Text.RegularExpressions.Regex.IsMatch(text, "\\A-?(0|[1-9][0-9]*)\\z"), "Invalid JSON literal/integer");
                literal.Clear();
            }
        }

        private static void CheckShape(JToken token, Type type)
        {
            if (token.Type == JTokenType.Null) return;
            if (type == typeof(string)) { UpdateValidation.Require(token.Type == JTokenType.String, "Expected JSON string"); return; }
            if (type == typeof(int) || type == typeof(long)) { UpdateValidation.Require(token.Type == JTokenType.Integer, "Expected JSON integer"); return; }
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(System.Collections.Generic.List<>))
            {
                UpdateValidation.Require(token is JArray, "Expected JSON array");
                foreach (JToken child in (JArray)token) CheckShape(child, type.GetGenericArguments()[0]);
                return;
            }
            UpdateValidation.Require(token is JObject, "Expected JSON object");
            var obj = (JObject)token;
            var fields = type.GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
            if (Array.Exists(fields, f => f.Name == "schema")) UpdateValidation.Require(obj.Property("schema", StringComparison.Ordinal) != null, "Missing schema");
            if (type == typeof(FileInventory) || type == typeof(PackageReceipt)) UpdateValidation.Require(obj.Property("files", StringComparison.Ordinal) != null, "Missing files");
            if (type == typeof(ModCatalog)) UpdateValidation.Require(obj.Property("packages", StringComparison.Ordinal) != null, "Missing packages");
            foreach (JProperty property in obj.Properties())
            {
                var field = Array.Find(fields, f => f.Name == property.Name);
                UpdateValidation.Require(field != null, "Unknown JSON field: " + property.Name);
                UpdateValidation.Require(property.Value.Type != JTokenType.Null || !field.FieldType.IsValueType, "Null scalar");
                CheckShape(property.Value, field.FieldType);
            }
        }

        private static void CheckTokens(JToken token)
        {
            UpdateValidation.Require(token.Type != JTokenType.Comment && token.Type != JTokenType.Undefined, "Nonstandard JSON");
            if (token is JObject obj)
            {
                foreach (JProperty property in obj.Properties())
                {
                    UpdateValidation.Require(!property.Name.StartsWith("$", StringComparison.Ordinal), "JSON metadata forbidden");
                    CheckTokens(property.Value);
                }
            }
            else if (token is JArray array) foreach (JToken item in array) CheckTokens(item);
        }

        private static void Validate<T>(T value)
        {
            UpdateValidation.Require(value != null, "Null JSON object");
            if (value is GameRelease release) UpdateValidation.ValidateBuild(release);
            else if (value is BuildInfo build) UpdateValidation.ValidateBuildInfo(build);
            else if (value is ModCatalog catalog) UpdateValidation.ValidateCatalog(catalog);
            else if (value is ModPackage package) UpdateValidation.ValidatePackage(package);
            else if (value is FileInventory inventory) { UpdateValidation.Require(inventory.schema == 1, "Unsupported inventory"); UpdateValidation.ValidateFiles(inventory.files); }
            else if (value is PackageReceipt receipt) { UpdateValidation.ValidatePackage(receipt.package); UpdateValidation.ValidateFiles(receipt.files); }
        }

        public static void Write<T>(string path, T value)
        {
            Validate(value);
            string full = Path.GetFullPath(path);
            UpdateValidation.NoLinks(full);
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            string temporary = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    using (var writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, true))
                    using (var json = new JsonTextWriter(writer) { Formatting = Formatting.Indented }) { Serializer().Serialize(json, value); json.Flush(); }
                    UpdateValidation.Require(stream.Length <= MaximumJsonBytes, "JSON too large");
                    stream.Flush(true);
                }
                UpdateValidation.NoLinks(full);
                if (File.Exists(full)) File.Replace(temporary, full, null);
                else File.Move(temporary, full);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}

using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.Json;

namespace CodePunk.Core.Utils;

public static class JsonSchemaGenerator
{
    public static JsonElement Generate<T>() => Generate(typeof(T));

    public static JsonElement Generate(Type type)
    {
        var properties = new Dictionary<string, object?>();
        var required = new List<string>();

        foreach (var prop in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            var schema = new Dictionary<string, object?>();
            var propType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;

            schema["type"] = MapType(propType);

            var display = prop.GetCustomAttribute<DisplayAttribute>();
            if (!string.IsNullOrWhiteSpace(display?.Description))
            {
                schema["description"] = display!.Description;
            }

            var minL = prop.GetCustomAttribute<MinLengthAttribute>();
            var maxL = prop.GetCustomAttribute<MaxLengthAttribute>();
            var strLen = prop.GetCustomAttribute<StringLengthAttribute>();
            if (minL != null) schema["minLength"] = minL.Length;
            if (maxL != null) schema["maxLength"] = maxL.Length;
            if (strLen != null)
            {
                if (strLen.MinimumLength > 0) schema["minLength"] = strLen.MinimumLength;
                if (strLen.MaximumLength > 0) schema["maxLength"] = strLen.MaximumLength;
            }

            var range = prop.GetCustomAttribute<RangeAttribute>();
            if (range != null)
            {
                if (double.TryParse(range.Minimum?.ToString(), out var min)) schema["minimum"] = min;
                if (double.TryParse(range.Maximum?.ToString(), out var max)) schema["maximum"] = max;
            }

            var regex = prop.GetCustomAttribute<RegularExpressionAttribute>();
            if (regex != null)
            {
                schema["pattern"] = regex.Pattern;
            }

            if (prop.GetCustomAttribute<RequiredAttribute>() != null)
            {
                required.Add(prop.Name);
            }

            properties[prop.Name] = schema;
        }

        var root = new Dictionary<string, object?>
        {
            ["type"] = "object",
            ["properties"] = properties,
        };
        if (required.Count > 0) root["required"] = required.ToArray();

        // Serialize to JsonElement
        var json = JsonSerializer.SerializeToElement(root, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        });
        return json;
    }

    private static object MapType(Type t)
    {
        if (t == typeof(string)) return "string";
        if (t == typeof(bool)) return "boolean";
        if (t == typeof(int) || t == typeof(long)) return "integer";
        if (t == typeof(float) || t == typeof(double) || t == typeof(decimal)) return "number";
        return "string";
    }
}

